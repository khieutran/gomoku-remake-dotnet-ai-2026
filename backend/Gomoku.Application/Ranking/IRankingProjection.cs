namespace Gomoku.Application.Ranking;

public interface IRankingProjection
{
    Task<IReadOnlyList<RankingScore>?> ReadTopAsync(int limit,
        CancellationToken cancellationToken = default);
    Task<RankingPosition?> ReadPositionAsync(Guid accountId,
        CancellationToken cancellationToken = default);
    Task<bool> IsReadyAsync(CancellationToken cancellationToken = default);
    Task<bool> RebuildAsync(IReadOnlyList<RankingScore> scores,
        CancellationToken cancellationToken = default);
    Task<bool> PublishAsync(Guid accountId, int points,
        CancellationToken cancellationToken = default);
}
