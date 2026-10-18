using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using System.Text.Json;

namespace SluiceBase.Api.Mcp.Clients;

internal abstract record FetchResult
{
    private FetchResult()
    {
    }

    public sealed record Ok(JsonElement Document, TimeSpan? MaxAge) : FetchResult;

    // The host answered, but the document is unusable (or the host is not publicly routable).
    public sealed record Invalid : FetchResult
    {
        public static Invalid Instance { get; } = new();
    }

    // No answer could be obtained; a cached last-good document may still be served.
    public sealed record Transient : FetchResult
    {
        public static Transient Instance { get; } = new();
    }
}

internal interface ICimdFetcher
{
    Task<FetchResult> FetchAsync(Uri url, CancellationToken ct);
}

internal sealed class CimdTargetRefusedException(string host)
    : Exception($"Refusing to connect to '{host}': it resolves to an address that is not publicly routable.");

internal sealed class CimdFetcher : ICimdFetcher
{
    public const string HttpClientName = "cimd";
    public const int MaxBodyBytes = 64 * 1024;
    public const int MaxConcurrentFetches = 4;
    public static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(5);

    private static readonly SemaphoreSlim SharedGate = new(MaxConcurrentFetches);

    private readonly HttpClient _client;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _gate;

    public CimdFetcher(IHttpClientFactory httpClientFactory)
        : this(httpClientFactory.CreateClient(HttpClientName), FetchTimeout, SharedGate)
    {
    }

    internal CimdFetcher(HttpMessageHandler handler, TimeSpan timeout, SemaphoreSlim gate)
        : this(new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan }, timeout, gate)
    {
    }

    private CimdFetcher(HttpClient client, TimeSpan timeout, SemaphoreSlim gate)
    {
        _client = client;
        _timeout = timeout;
        _gate = gate;
    }

    public async Task<FetchResult> FetchAsync(Uri url, CancellationToken ct)
    {
        if (!_gate.Wait(0, CancellationToken.None))
        {
            return FetchResult.Transient.Instance;
        }

        try
        {
            // The deadline covers the body read too; HttpClient.Timeout stops at the headers
            // under ResponseHeadersRead.
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(_timeout);
            return await FetchCoreAsync(url, deadline.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return FetchResult.Transient.Instance;
        }
        catch (HttpRequestException ex) when (IsRefusedTarget(ex))
        {
            return FetchResult.Invalid.Instance;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or SocketException)
        {
            return FetchResult.Transient.Instance;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<FetchResult> FetchCoreAsync(Uri url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        if ((int)response.StatusCode >= 500)
        {
            return FetchResult.Transient.Instance;
        }

        if (response.StatusCode != HttpStatusCode.OK
            || !IsJsonMediaType(response.Content.Headers.ContentType?.MediaType)
            || response.Content.Headers.ContentLength > MaxBodyBytes)
        {
            return FetchResult.Invalid.Instance;
        }

        var body = await ReadBoundedAsync(response.Content, ct);
        if (body is null)
        {
            return FetchResult.Invalid.Instance;
        }

        try
        {
            using var document = JsonDocument.Parse(body.Value);
            return new FetchResult.Ok(document.RootElement.Clone(), response.Headers.CacheControl?.MaxAge);
        }
        catch (JsonException)
        {
            return FetchResult.Invalid.Instance;
        }
    }

    private static async Task<ReadOnlyMemory<byte>?> ReadBoundedAsync(HttpContent content, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct);
        var buffer = new byte[MaxBodyBytes + 1];
        var length = 0;

        while (length < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length), ct);
            if (read == 0)
            {
                return buffer.AsMemory(0, length);
            }

            length += read;
        }

        return null;
    }

    private static bool IsJsonMediaType(string? mediaType)
    {
        return mediaType is not null
            && (mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
                || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsRefusedTarget(Exception ex)
    {
        for (var e = ex.InnerException; e is not null; e = e.InnerException)
        {
            if (e is CimdTargetRefusedException)
            {
                return true;
            }
        }

        return false;
    }

    public static SocketsHttpHandler CreateHandler(bool allowLoopback)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectCallback = (context, ct) =>
                ConnectAsync(context.DnsEndPoint, allowLoopback, Dns.GetHostAddressesAsync, ct),
        };

        if (allowLoopback)
        {
            handler.SslOptions.RemoteCertificateValidationCallback = (sender, _, _, errors) =>
                IsCertificateAcceptable((sender as SslStream)?.TargetHostName ?? "", errors, allowLoopback);
        }

        return handler;
    }

    // Resolving and vetting at connect time (rather than before the request) defeats DNS rebinding.
    internal static async ValueTask<Stream> ConnectAsync(
        DnsEndPoint endpoint,
        bool allowLoopback,
        Func<string, CancellationToken, Task<IPAddress[]>> resolve,
        CancellationToken ct)
    {
        var addresses = await resolve(endpoint.Host, ct);
        var target = SelectVettedAddress(endpoint.Host, addresses, allowLoopback);

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(target, endpoint.Port), ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    internal static IPAddress SelectVettedAddress(string host, IReadOnlyList<IPAddress> addresses, bool allowLoopback)
    {
        if (addresses.Count == 0)
        {
            throw new SocketException((int)SocketError.HostNotFound);
        }

        // A loopback host name must really land on loopback: the relaxed certificate check below
        // trusts the host name, so it must not be able to reach anything else.
        var loopbackHost = allowLoopback && IsLoopbackHost(host);

        foreach (var address in addresses)
        {
            var permitted = loopbackHost
                ? IPAddress.IsLoopback(address)
                : PublicAddress.IsPublic(address) || (allowLoopback && IPAddress.IsLoopback(address));
            if (!permitted)
            {
                throw new CimdTargetRefusedException(host);
            }
        }

        return addresses[0];
    }

    internal static bool IsCertificateAcceptable(string targetHost, SslPolicyErrors errors, bool allowLoopback)
    {
        return errors == SslPolicyErrors.None || (allowLoopback && IsLoopbackHost(targetHost));
    }

    private static bool IsLoopbackHost(string host)
    {
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || (IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip));
    }
}
