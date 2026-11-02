using System.Collections.Concurrent;

namespace SluiceBase.Api.Mcp.Clients;

internal sealed class CimdClientSource(ICimdFetcher fetcher, TimeProvider time) : ICimdClientSource
{
    public const int MaxEntries = 1000;

    private static readonly TimeSpan MinTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaxTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(1);
    private static readonly TimeSpan NegativeTtl = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan StaleGrace = TimeSpan.FromDays(7);

    private readonly ConcurrentDictionary<string, Entry> _cache = new(StringComparer.Ordinal);

    internal int EntryCount => _cache.Count;

    public async Task<ClientResolution> ResolveAsync(Uri clientIdUrl, CancellationToken ct)
    {
        var key = clientIdUrl.OriginalString;
        if (!CimdDocument.IsAcceptableClientIdUrl(key, out var url))
        {
            return ClientResolution.NotFound.Instance;
        }

        var now = time.GetUtcNow();
        _cache.TryGetValue(key, out var entry);

        if (entry is not null)
        {
            if (entry.Client is not null && now < entry.FreshUntil)
            {
                return new ClientResolution.Found(entry.Client);
            }

            if (entry.Client is null && now < entry.NegativeUntil)
            {
                return ClientResolution.NotFound.Instance;
            }
        }

        var result = await fetcher.FetchAsync(url, ct);
        now = time.GetUtcNow();

        switch (result)
        {
            case FetchResult.Ok ok when CimdDocument.Validate(url, ok.Document) is { } client:
                var ttl = Math.Clamp((ok.MaxAge ?? DefaultTtl).Ticks, MinTtl.Ticks, MaxTtl.Ticks);
                var fresh = now + TimeSpan.FromTicks(ttl);
                Store(key, new Entry(client, fresh, fresh + StaleGrace, default), now);
                return new ClientResolution.Found(client);

            case FetchResult.Transient when entry?.Client is { } lastGood && now < entry.StaleUntil:
                return new ClientResolution.Found(lastGood);

            case FetchResult.Transient:
                return ClientResolution.TemporarilyUnavailable.Instance;

            default:
                Store(key, new Entry(null, default, default, now + NegativeTtl), now);
                return ClientResolution.NotFound.Instance;
        }
    }

    private void Store(string key, Entry entry, DateTimeOffset now)
    {
        if (_cache.Count >= MaxEntries && !_cache.ContainsKey(key))
        {
            Sweep(now);
        }

        _cache[key] = entry;
    }

    private void Sweep(DateTimeOffset now)
    {
        foreach (var (k, e) in _cache)
        {
            var expired = e.Client is null ? now >= e.NegativeUntil : now >= e.StaleUntil;
            if (expired)
            {
                _cache.TryRemove(k, out _);
            }
        }

        if (_cache.Count < MaxEntries)
        {
            return;
        }

        // Still full of live entries: drop the ones closest to expiry.
        foreach (var k in _cache.OrderBy(p => p.Value.Client is null ? p.Value.NegativeUntil : p.Value.FreshUntil)
                     .Take(MaxEntries / 10)
                     .Select(p => p.Key))
        {
            _cache.TryRemove(k, out _);
        }
    }

    private sealed record Entry(
        ResolvedClient? Client,
        DateTimeOffset FreshUntil,
        DateTimeOffset StaleUntil,
        DateTimeOffset NegativeUntil);
}
