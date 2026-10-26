using Gomoku.Application.Ranking;

namespace Gomoku.Infrastructure.Ranking;

public sealed class NoopRankingProjection : IRankingProjection
{
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
    public Task<IReadOnlyList<RankingScore>?> ReadTopAsync(int limit,
        CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RankingScore>?>(null);
    public Task<RankingPosition?> ReadPositionAsync(Guid accountId,
        CancellationToken cancellationToken = default) => Task.FromResult<RankingPosition?>(null);
    public Task<bool> IsReadyAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
    public Task<bool> RebuildAsync(IReadOnlyList<RankingScore> scores,
        CancellationToken cancellationToken = default) => Task.FromResult(false);
    public Task<bool> PublishAsync(Guid accountId, int points,
        CancellationToken cancellationToken = default) => Task.FromResult(false);
}
