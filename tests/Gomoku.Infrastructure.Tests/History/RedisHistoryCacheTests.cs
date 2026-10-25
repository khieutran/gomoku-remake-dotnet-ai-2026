using Gomoku.Application.History;
using Gomoku.Infrastructure.History;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Gomoku.Infrastructure.Tests.History;

public sealed class RedisHistoryCacheTests
{
    [Fact]
    public async Task RoundTripsPageAndDetailWithBoundedTtl()
    {
        var storage = new FakeDistributedCache();
        var cache = new RedisHistoryCache(storage, NullLogger<RedisHistoryCache>.Instance);
        var gameId = Guid.NewGuid();
        var matchId = Guid.NewGuid();
        var time = new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);
        var summary = new HistoryGameSummary(gameId, matchId, 1, "XWon", "X",
            "gomoku-exact-five-v1", 20, null, time.AddMinutes(-1), time, 9);
        var page = new HistoryPage([summary], "next");
        var detail = new HistoryGameDetail(gameId, matchId, 1, "XWon", "X",
            summary.RuleVersion, 20, null, summary.StartedAt, time,
            [new HistoryMove(1, 0, 0, "X", "Human", time.AddMinutes(-1))]);

        await cache.SetPageAsync("page", page, TimeSpan.FromMinutes(5));
        await cache.SetGameAsync("game", detail, TimeSpan.FromHours(1));
        Assert.Equal(TimeSpan.FromMinutes(5), storage.Options["page"].AbsoluteExpirationRelativeToNow);
        Assert.Equal(TimeSpan.FromHours(1), storage.Options["game"].AbsoluteExpirationRelativeToNow);
        Assert.Equal(gameId, (await cache.GetPageAsync("page"))!.Items[0].GameId);
        Assert.Equal("Human", (await cache.GetGameAsync("game"))!.Moves[0].Actor);
    }

    [Fact]
    public async Task RedisFailureBecomesCacheMissAndDoesNotBlockWritePath()
    {
        var cache = new RedisHistoryCache(new FakeDistributedCache { Fail = true },
            NullLogger<RedisHistoryCache>.Instance);
        Assert.Null(await cache.GetPageAsync("page"));
        Assert.Null(await cache.GetGameAsync("game"));
        await cache.SetPageAsync("page", new HistoryPage([], null), TimeSpan.FromMinutes(5));
        await cache.SetGameAsync("game", new HistoryGameDetail(Guid.NewGuid(), Guid.NewGuid(),
            1, "Draw", "X", "v1", 20, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, []),
            TimeSpan.FromHours(1));
    }

    private sealed class FakeDistributedCache : IDistributedCache
    {
        private readonly Dictionary<string, byte[]> _values = new();
        public readonly Dictionary<string, DistributedCacheEntryOptions> Options = new();
        public bool Fail { get; set; }

        public byte[]? Get(string key) => Fail ? throw new InvalidOperationException("Redis down")
            : _values.GetValueOrDefault(key);

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
            Task.FromResult(Get(key));

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            if (Fail) throw new InvalidOperationException("Redis down");
            _values[key] = value;
            Options[key] = options;
        }

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options,
            CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }

        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) => _values.Remove(key);
        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }
    }
}
