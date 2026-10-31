using Gomoku.Application.Ranking;
using Xunit;

namespace Gomoku.Application.Tests.Ranking;

public sealed class RankingServiceTests
{
    private static readonly Guid AccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task UnpublishedResultUsesPostgresBeforeRedisCatchesUp()
    {
        var service = new RankingService(new Reader(pending: true), new Projection());

        Assert.Equal(3, Assert.Single((await service.TopAsync(20)).Items).Points);
        Assert.Equal(3, (await service.MineAsync(AccountId))!.Points);
    }

    [Fact]
    public async Task FullyPublishedResultUsesRedisProjection()
    {
        var service = new RankingService(new Reader(pending: false), new Projection());

        Assert.Equal(1, Assert.Single((await service.TopAsync(20)).Items).Points);
        Assert.Equal(1, (await service.MineAsync(AccountId))!.Points);
    }

    [Fact]
    public async Task LeaderboardEntriesCarryDisplayNames()
    {
        var service = new RankingService(new Reader(pending: true), new Projection());

        Assert.Equal("Kiô", Assert.Single((await service.TopAsync(20)).Items).DisplayName);
    }

    [Fact]
    public async Task EqualScoresShareRankAndNextRankSkipsPositions()
    {
        var scores = new RankingScore[]
        {
            new(Guid.NewGuid(), 6), new(Guid.NewGuid(), 3),
            new(Guid.NewGuid(), 3), new(Guid.NewGuid(), 0)
        };
        var service = new RankingService(new Reader(pending: true, scores: scores), new Projection());

        Assert.Equal([1, 2, 2, 4], (await service.TopAsync(20)).Items.Select(x => x.Rank));
    }

    private sealed class Reader(bool pending, IReadOnlyList<RankingScore>? scores = null) : IRankingReader
    {
        public Task<bool> HasPendingAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(pending);
        public Task<IReadOnlyList<RankingScore>> ReadTopAsync(int limit,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RankingScore>>(scores ?? [new(AccountId, 3)]);
        public Task<RankingPosition?> ReadPositionAsync(Guid accountId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<RankingPosition?>(new(AccountId, 3, 1));
        public Task<IReadOnlyList<RankingScore>> ReadAllAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RankingScore>>([new(AccountId, 3)]);
        public Task<IReadOnlyDictionary<Guid, string>> ReadDisplayNamesAsync(IReadOnlyCollection<Guid> accountIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string> { [AccountId] = "Kiô" });
    }

    private sealed class Projection : IRankingProjection
    {
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
        public Task<IReadOnlyList<RankingScore>?> ReadTopAsync(int limit,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RankingScore>?>([new(AccountId, 1)]);
        public Task<RankingPosition?> ReadPositionAsync(Guid accountId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<RankingPosition?>(new(AccountId, 1, 1));
        public Task<bool> IsReadyAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
        public Task<bool> RebuildAsync(IReadOnlyList<RankingScore> scores,
            CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> PublishAsync(Guid accountId, int points,
            CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
