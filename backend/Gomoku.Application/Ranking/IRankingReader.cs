namespace Gomoku.Application.Ranking;

public interface IRankingReader
{
    Task<bool> HasPendingAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RankingScore>> ReadTopAsync(int limit,
        CancellationToken cancellationToken = default);
    Task<RankingPosition?> ReadPositionAsync(Guid accountId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RankingScore>> ReadAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<Guid, string>> ReadDisplayNamesAsync(IReadOnlyCollection<Guid> accountIds,
        CancellationToken cancellationToken = default);
}
