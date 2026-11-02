using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using SluiceBase.Api.Mcp.Clients;

namespace SluiceBase.Api.Tests;

public class CimdClientSourceTests
{
    private const string ClientId = "https://client.example/meta.json";
    private static readonly Uri ClientUrl = new(ClientId);

    private readonly FakeTimeProvider _time = new();
    private readonly FakeFetcher _fetcher = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private CimdClientSource CreateSource() => new(_fetcher, _time);

    private static FetchResult.Ok ValidDocument(TimeSpan? maxAge) => new(
        JsonDocument.Parse(
            $$"""{"client_id":"{{ClientId}}","client_name":"Test","token_endpoint_auth_method":"none","redirect_uris":["https://client.example/cb"]}""")
            .RootElement,
        maxAge);

    [Fact]
    public async Task Resolve_CachesPositive_ForMaxAgeClamped()
    {
        _fetcher.Next = ValidDocument(TimeSpan.FromSeconds(60));
        var source = CreateSource();

        Assert.IsType<ClientResolution.Found>(await source.ResolveAsync(ClientUrl, Ct));
        _time.Advance(TimeSpan.FromMinutes(4));
        await source.ResolveAsync(ClientUrl, Ct);
        Assert.Equal(1, _fetcher.Calls);

        _time.Advance(TimeSpan.FromMinutes(2));
        await source.ResolveAsync(ClientUrl, Ct);
        Assert.Equal(2, _fetcher.Calls);
    }

    [Fact]
    public async Task Resolve_MaxAgeAbove24h_ClampedTo24h()
    {
        _fetcher.Next = ValidDocument(TimeSpan.FromDays(30));
        var source = CreateSource();

        await source.ResolveAsync(ClientUrl, Ct);
        _time.Advance(TimeSpan.FromHours(23));
        await source.ResolveAsync(ClientUrl, Ct);
        Assert.Equal(1, _fetcher.Calls);

        _time.Advance(TimeSpan.FromHours(2));
        await source.ResolveAsync(ClientUrl, Ct);
        Assert.Equal(2, _fetcher.Calls);
    }

    [Fact]
    public async Task Resolve_NoMaxAge_Defaults1h()
    {
        _fetcher.Next = ValidDocument(null);
        var source = CreateSource();

        await source.ResolveAsync(ClientUrl, Ct);
        _time.Advance(TimeSpan.FromMinutes(59));
        await source.ResolveAsync(ClientUrl, Ct);
        Assert.Equal(1, _fetcher.Calls);

        _time.Advance(TimeSpan.FromMinutes(2));
        await source.ResolveAsync(ClientUrl, Ct);
        Assert.Equal(2, _fetcher.Calls);
    }

    [Fact]
    public async Task Resolve_InvalidDocument_NotFound_CachedFor1Min()
    {
        _fetcher.Next = FetchResult.Invalid.Instance;
        var source = CreateSource();

        Assert.IsType<ClientResolution.NotFound>(await source.ResolveAsync(ClientUrl, Ct));
        _time.Advance(TimeSpan.FromSeconds(30));
        await source.ResolveAsync(ClientUrl, Ct);
        Assert.Equal(1, _fetcher.Calls);

        _time.Advance(TimeSpan.FromSeconds(31));
        await source.ResolveAsync(ClientUrl, Ct);
        Assert.Equal(2, _fetcher.Calls);
    }

    [Fact]
    public async Task Resolve_OkButFailsValidation_NotFound_NegativeCached()
    {
        _fetcher.Next = new FetchResult.Ok(JsonDocument.Parse("""{"client_id":"https://other.example/x"}""").RootElement, null);
        var source = CreateSource();

        Assert.IsType<ClientResolution.NotFound>(await source.ResolveAsync(ClientUrl, Ct));
        await source.ResolveAsync(ClientUrl, Ct);
        Assert.Equal(1, _fetcher.Calls);
    }

    [Fact]
    public async Task Resolve_InvalidAfterGood_DiscardsLastGood()
    {
        _fetcher.Next = ValidDocument(null);
        var source = CreateSource();
        await source.ResolveAsync(ClientUrl, Ct);

        _time.Advance(TimeSpan.FromHours(2));
        _fetcher.Next = FetchResult.Invalid.Instance;
        Assert.IsType<ClientResolution.NotFound>(await source.ResolveAsync(ClientUrl, Ct));

        _time.Advance(TimeSpan.FromMinutes(2));
        _fetcher.Next = FetchResult.Transient.Instance;
        Assert.IsType<ClientResolution.TemporarilyUnavailable>(await source.ResolveAsync(ClientUrl, Ct));
    }

    [Fact]
    public async Task Resolve_TransientWithLastGood_ServesStale()
    {
        _fetcher.Next = ValidDocument(null);
        var source = CreateSource();
        await source.ResolveAsync(ClientUrl, Ct);

        _time.Advance(TimeSpan.FromHours(2));
        _fetcher.Next = FetchResult.Transient.Instance;

        Assert.IsType<ClientResolution.Found>(await source.ResolveAsync(ClientUrl, Ct));
        Assert.Equal(2, _fetcher.Calls);
    }

    [Fact]
    public async Task Resolve_TransientBeyond7DaysStale_TemporarilyUnavailable()
    {
        _fetcher.Next = ValidDocument(null);
        var source = CreateSource();
        await source.ResolveAsync(ClientUrl, Ct);

        _time.Advance(TimeSpan.FromHours(1) + TimeSpan.FromDays(7) + TimeSpan.FromMinutes(1));
        _fetcher.Next = FetchResult.Transient.Instance;

        Assert.IsType<ClientResolution.TemporarilyUnavailable>(await source.ResolveAsync(ClientUrl, Ct));
    }

    [Fact]
    public async Task Resolve_TransientNoLastGood_TemporarilyUnavailable_NotNegativeCached()
    {
        _fetcher.Next = FetchResult.Transient.Instance;
        var source = CreateSource();

        Assert.IsType<ClientResolution.TemporarilyUnavailable>(await source.ResolveAsync(ClientUrl, Ct));
        await source.ResolveAsync(ClientUrl, Ct);
        Assert.Equal(2, _fetcher.Calls);
    }

    [Fact]
    public async Task Resolve_UnacceptableUrl_NotFound_NoFetch()
    {
        var source = CreateSource();

        var result = await source.ResolveAsync(new Uri("https://client.example/"), Ct);

        Assert.IsType<ClientResolution.NotFound>(result);
        Assert.Equal(0, _fetcher.Calls);
    }

    [Fact]
    public async Task Resolve_ManyDistinctIds_StaysBounded()
    {
        _fetcher.Next = FetchResult.Invalid.Instance;
        var source = CreateSource();

        for (var i = 0; i < CimdClientSource.MaxEntries + 50; i++)
        {
            await source.ResolveAsync(new Uri($"https://client.example/{i}.json"), Ct);
            _time.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.True(source.EntryCount <= CimdClientSource.MaxEntries);
    }

    private sealed class FakeFetcher : ICimdFetcher
    {
        public FetchResult Next { get; set; } = FetchResult.Invalid.Instance;
        public int Calls { get; private set; }

        public Task<FetchResult> FetchAsync(Uri url, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(Next);
        }
    }
}
