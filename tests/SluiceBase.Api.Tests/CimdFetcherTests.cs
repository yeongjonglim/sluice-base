using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using SluiceBase.Api.Mcp.Clients;

namespace SluiceBase.Api.Tests;

public class CimdFetcherTests
{
    private static readonly Uri Url = new("https://client.example/cimd.json");
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }

    private static CimdFetcher Fetcher(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond,
        TimeSpan? timeout = null,
        SemaphoreSlim? gate = null)
    {
        return new CimdFetcher(new StubHandler(respond), timeout ?? TestTimeout, gate ?? new SemaphoreSlim(4));
    }

    [Fact]
    public async Task Fetch_200Json_Ok_ReadsMaxAge()
    {
        HttpRequestMessage? seen = null;
        var fetcher = Fetcher((req, _) =>
        {
            seen = req;
            var response = Json("""{"client_id":"https://client.example/cimd.json"}""");
            response.Headers.CacheControl = CacheControlHeaderValue.Parse("max-age=600");
            return Task.FromResult(response);
        });

        var result = await fetcher.FetchAsync(Url, TestContext.Current.CancellationToken);

        var ok = Assert.IsType<FetchResult.Ok>(result);
        Assert.Equal(TimeSpan.FromMinutes(10), ok.MaxAge);
        Assert.Equal("https://client.example/cimd.json", ok.Document.GetProperty("client_id").GetString());
        Assert.Equal(HttpMethod.Get, seen!.Method);
        Assert.Contains(seen.Headers.Accept, a => a.MediaType == "application/json");
    }

    [Fact]
    public async Task Fetch_NoCacheControl_MaxAgeNull()
    {
        var fetcher = Fetcher((_, _) => Task.FromResult(Json("{}")));

        var result = await fetcher.FetchAsync(Url, TestContext.Current.CancellationToken);

        Assert.Null(Assert.IsType<FetchResult.Ok>(result).MaxAge);
    }

    [Theory]
    [InlineData("application/json; charset=utf-8")]
    [InlineData("Application/JSON")]
    [InlineData("application/oauth-client+json")]
    public async Task Fetch_JsonContentTypes_Ok(string contentType)
    {
        var fetcher = Fetcher((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
            response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
            return Task.FromResult(response);
        });

        Assert.IsType<FetchResult.Ok>(await fetcher.FetchAsync(Url, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(HttpStatusCode.MovedPermanently)]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.TemporaryRedirect)]
    public async Task Fetch_3xx_Invalid(HttpStatusCode status)
    {
        var fetcher = Fetcher((_, _) =>
        {
            var response = new HttpResponseMessage(status);
            response.Headers.Location = new Uri("http://169.254.169.254/latest/meta-data");
            return Task.FromResult(response);
        });

        Assert.IsType<FetchResult.Invalid>(await fetcher.FetchAsync(Url, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Fetch_404_Invalid()
    {
        var fetcher = Fetcher((_, _) => Task.FromResult(Json("{}", HttpStatusCode.NotFound)));

        Assert.IsType<FetchResult.Invalid>(await fetcher.FetchAsync(Url, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Fetch_Non200Success_Invalid()
    {
        var fetcher = Fetcher((_, _) => Task.FromResult(Json("{}", HttpStatusCode.Accepted)));

        Assert.IsType<FetchResult.Invalid>(await fetcher.FetchAsync(Url, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("text/html")]
    [InlineData("text/plain")]
    [InlineData("application/jsonp")]
    [InlineData(null)]
    public async Task Fetch_NonJsonContentType_Invalid(string? contentType)
    {
        var fetcher = Fetcher((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent("{}"u8.ToArray()) };
            response.Content.Headers.ContentType = contentType is null ? null : MediaTypeHeaderValue.Parse(contentType);
            return Task.FromResult(response);
        });

        Assert.IsType<FetchResult.Invalid>(await fetcher.FetchAsync(Url, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Fetch_MalformedJson_Invalid()
    {
        var fetcher = Fetcher((_, _) => Task.FromResult(Json("""{"client_id": """)));

        Assert.IsType<FetchResult.Invalid>(await fetcher.FetchAsync(Url, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Fetch_RetryableStatus_Transient(HttpStatusCode status)
    {
        var fetcher = Fetcher((_, _) => Task.FromResult(Json("{}", status)));

        Assert.IsType<FetchResult.Transient>(await fetcher.FetchAsync(Url, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Fetch_Timeout_Transient()
    {
        var fetcher = Fetcher(
            async (_, ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return Json("{}");
            },
            timeout: TimeSpan.FromMilliseconds(100));

        Assert.IsType<FetchResult.Transient>(await fetcher.FetchAsync(Url, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Fetch_SlowBody_Timeout_Transient()
    {
        var fetcher = Fetcher(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new StallingStream())
                {
                    Headers = { ContentType = new MediaTypeHeaderValue("application/json") },
                },
            }),
            timeout: TimeSpan.FromMilliseconds(100));

        Assert.IsType<FetchResult.Transient>(await fetcher.FetchAsync(Url, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Fetch_CallerCancelled_Throws()
    {
        using var cts = new CancellationTokenSource();
        var fetcher = Fetcher(async (_, ct) =>
        {
            await cts.CancelAsync();
            await Task.Delay(Timeout.Infinite, ct);
            return Json("{}");
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetcher.FetchAsync(Url, cts.Token));
    }

    [Fact]
    public async Task Fetch_HttpRequestException_Transient()
    {
        var fetcher = Fetcher((_, _) => throw new HttpRequestException(
            "connection refused", new SocketException((int)SocketError.ConnectionRefused)));

        Assert.IsType<FetchResult.Transient>(await fetcher.FetchAsync(Url, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Fetch_ConnectGuardRefusal_Invalid()
    {
        var fetcher = Fetcher((_, _) => throw new HttpRequestException(
            "refused", new CimdTargetRefusedException("client.example")));

        Assert.IsType<FetchResult.Invalid>(await fetcher.FetchAsync(Url, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Fetch_ChunkedBodyOver64KiB_Invalid()
    {
        var body = new CountingStream(70 * 1024);
        var fetcher = Fetcher((_, _) =>
        {
            var content = new StreamContent(body);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            Assert.Null(content.Headers.ContentLength);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });

        var result = await fetcher.FetchAsync(Url, TestContext.Current.CancellationToken);

        Assert.IsType<FetchResult.Invalid>(result);
        Assert.True(body.BytesRead < 70 * 1024, $"read {body.BytesRead} bytes; expected the read to stop past 64 KiB");
    }

    [Fact]
    public async Task Fetch_DeclaredContentLengthOver64KiB_Invalid()
    {
        var fetcher = Fetcher((_, _) => Task.FromResult(Json(new string(' ', 65 * 1024) + "{}")));

        Assert.IsType<FetchResult.Invalid>(await fetcher.FetchAsync(Url, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Fetch_BodyExactly64KiB_Ok()
    {
        var fetcher = Fetcher((_, _) => Task.FromResult(Json(new string(' ', (64 * 1024) - 2) + "{}")));

        Assert.IsType<FetchResult.Ok>(await fetcher.FetchAsync(Url, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Fetch_FifthConcurrent_Transient()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = 0;
        var gate = new SemaphoreSlim(4);
        var fetcher = Fetcher(
            async (_, _) =>
            {
                if (Interlocked.Increment(ref entered) == 4)
                {
                    allEntered.SetResult();
                }

                await release.Task;
                return Json("{}");
            },
            gate: gate);
        var ct = TestContext.Current.CancellationToken;

        var inFlight = Enumerable.Range(0, 4).Select(_ => fetcher.FetchAsync(Url, ct)).ToList();
        await allEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);

        var fifth = fetcher.FetchAsync(Url, ct);
        Assert.True(fifth.IsCompleted);
        Assert.IsType<FetchResult.Transient>(await fifth);
        Assert.Equal(4, entered);

        release.SetResult();
        foreach (var result in await Task.WhenAll(inFlight))
        {
            Assert.IsType<FetchResult.Ok>(result);
        }

        Assert.Equal(4, gate.CurrentCount);
    }

    [Fact]
    public async Task Fetch_ReleasesGateOnFailure()
    {
        var gate = new SemaphoreSlim(4);
        var fetcher = Fetcher((_, _) => throw new HttpRequestException("boom"), gate: gate);

        for (var i = 0; i < 6; i++)
        {
            Assert.IsType<FetchResult.Transient>(await fetcher.FetchAsync(Url, TestContext.Current.CancellationToken));
        }

        Assert.Equal(4, gate.CurrentCount);
    }

    [Fact]
    public async Task ConnectGuard_RefusesPrivateResolution()
    {
        var resolverCalled = false;

        await Assert.ThrowsAsync<CimdTargetRefusedException>(async () =>
        {
            await using var stream = await CimdFetcher.ConnectAsync(
                new DnsEndPoint("client.example", 443),
                allowLoopback: false,
                (_, _) =>
                {
                    resolverCalled = true;
                    return Task.FromResult(new[] { IPAddress.Parse("1.1.1.1"), IPAddress.Parse("10.0.0.1") });
                },
                TestContext.Current.CancellationToken);
        });

        Assert.True(resolverCalled);
    }

    [Theory]
    [InlineData("10.0.0.1")]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    [InlineData("100.64.1.1")]
    [InlineData("127.0.0.1")]
    public void VetAddresses_RefusesNonPublic(string address)
    {
        Assert.Throws<CimdTargetRefusedException>(() =>
            CimdFetcher.VetAddresses("client.example", [IPAddress.Parse(address)], allowLoopback: false));
    }

    [Fact]
    public void VetAddresses_MixedPublicAndPrivate_Refuses()
    {
        Assert.Throws<CimdTargetRefusedException>(() => CimdFetcher.VetAddresses(
            "client.example",
            [IPAddress.Parse("10.0.0.1"), IPAddress.Parse("1.1.1.1")],
            allowLoopback: false));
    }

    [Fact]
    public void VetAddresses_AllPublic_ReturnsAllInOrder()
    {
        var vetted = CimdFetcher.VetAddresses(
            "client.example",
            [IPAddress.Parse("2606:4700:4700::1111"), IPAddress.Parse("1.1.1.1")],
            allowLoopback: false);

        Assert.Equal([IPAddress.Parse("2606:4700:4700::1111"), IPAddress.Parse("1.1.1.1")], vetted);
    }

    [Fact]
    public void VetAddresses_NoAddresses_IsDnsFailureNotRefusal()
    {
        Assert.Throws<SocketException>(() =>
            CimdFetcher.VetAddresses("client.example", [], allowLoopback: false));
    }

    [Fact]
    public void VetAddresses_AllowLoopback_PermitsLoopbackButNotPrivate()
    {
        Assert.Equal(
            [IPAddress.Loopback],
            CimdFetcher.VetAddresses("localhost", [IPAddress.Loopback], allowLoopback: true));
        Assert.Throws<CimdTargetRefusedException>(() =>
            CimdFetcher.VetAddresses("client.example", [IPAddress.Parse("10.0.0.1")], allowLoopback: true));
        Assert.Throws<CimdTargetRefusedException>(() =>
            CimdFetcher.VetAddresses("localhost", [IPAddress.Parse("1.1.1.1")], allowLoopback: true));
    }

    [Fact]
    public async Task ConnectGuard_AllowLoopback_ConnectsToLoopback()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var ct = TestContext.Current.CancellationToken;

        var accept = listener.AcceptTcpClientAsync(ct);
        await using var stream = await CimdFetcher.ConnectAsync(
            new DnsEndPoint("localhost", port),
            allowLoopback: true,
            (_, _) => Task.FromResult(new[] { IPAddress.Loopback }),
            ct);
        using var accepted = await accept;

        Assert.True(accepted.Connected);
    }

    [Fact]
    public async Task ConnectGuard_FirstAddressFails_FallsBackToNext()
    {
        var attempted = new List<IPAddress>();
        var second = new MemoryStream();

        var stream = await CimdFetcher.ConnectAsync(
            new DnsEndPoint("client.example", 443),
            allowLoopback: false,
            (_, _) => Task.FromResult(new[] { IPAddress.Parse("2606:4700:4700::1111"), IPAddress.Parse("1.1.1.1") }),
            (endpoint, _) =>
            {
                Assert.Equal(443, endpoint.Port);
                attempted.Add(endpoint.Address);
                return attempted.Count == 1
                    ? ValueTask.FromException<Stream>(new SocketException((int)SocketError.NetworkUnreachable))
                    : ValueTask.FromResult<Stream>(second);
            },
            TestContext.Current.CancellationToken);

        Assert.Same(second, stream);
        Assert.Equal([IPAddress.Parse("2606:4700:4700::1111"), IPAddress.Parse("1.1.1.1")], attempted);
    }

    [Fact]
    public async Task ConnectGuard_AllAddressesFail_Throws()
    {
        var attempts = 0;

        await Assert.ThrowsAsync<SocketException>(async () => await CimdFetcher.ConnectAsync(
            new DnsEndPoint("client.example", 443),
            allowLoopback: false,
            (_, _) => Task.FromResult(new[] { IPAddress.Parse("1.1.1.1"), IPAddress.Parse("1.0.0.1") }),
            (_, _) =>
            {
                attempts++;
                return ValueTask.FromException<Stream>(new SocketException((int)SocketError.ConnectionRefused));
            },
            TestContext.Current.CancellationToken));

        Assert.Equal(2, attempts);
    }

    [Theory]
    [InlineData("localhost", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("client.example", false)]
    public void LoopbackCertificateValidation_AcceptsOnlyLoopbackHosts(string targetHost, bool accepted)
    {
        Assert.True(CimdFetcher.IsCertificateAcceptable(targetHost, SslPolicyErrors.None, allowLoopback: true));
        Assert.Equal(accepted, CimdFetcher.IsCertificateAcceptable(
            targetHost, SslPolicyErrors.RemoteCertificateChainErrors, allowLoopback: true));
        Assert.False(CimdFetcher.IsCertificateAcceptable(
            targetHost, SslPolicyErrors.RemoteCertificateChainErrors, allowLoopback: false));
    }

    [Theory]
    [InlineData("https://127.0.0.1/cimd.json")]
    [InlineData("https://[::1]/cimd.json")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("https://[::ffff:10.0.0.1]/cimd.json")]
    public async Task Fetch_RealHandler_NonPublicTarget_Invalid(string url)
    {
        var fetcher = new CimdFetcher(CimdFetcher.CreateHandler(allowLoopback: false), TestTimeout, new SemaphoreSlim(4));

        Assert.IsType<FetchResult.Invalid>(await fetcher.FetchAsync(new Uri(url), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void CreateHandler_DisablesRedirectsAndProxy()
    {
        using var handler = CimdFetcher.CreateHandler(allowLoopback: false);

        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseProxy);
        Assert.Equal(DecompressionMethods.All, handler.AutomaticDecompression);
        Assert.NotNull(handler.ConnectCallback);
        Assert.Null(handler.SslOptions.RemoteCertificateValidationCallback);
    }

    [Fact]
    public void CreateHandler_AllowLoopback_SetsCertificateCallback()
    {
        using var handler = CimdFetcher.CreateHandler(allowLoopback: true);

        Assert.NotNull(handler.SslOptions.RemoteCertificateValidationCallback);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return respond(request, cancellationToken);
        }
    }

    // Non-seekable with no length, so StreamContent reports no Content-Length (as with chunked encoding).
    private sealed class CountingStream(int total) : Stream
    {
        public int BytesRead { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = Math.Min(count, Math.Min(total - BytesRead, 1024));
            buffer.AsSpan(offset, n).Fill((byte)' ');
            BytesRead += n;
            return n;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class StallingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
