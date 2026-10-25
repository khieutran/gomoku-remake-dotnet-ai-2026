using Gomoku.Application.Identity;

namespace Gomoku.Application.History;

public interface IHistoryReader
{
    Task<long> ReadRevisionAsync(OwnerIdentity owner, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HistoryGameSummary>> ReadCompletedAsync(OwnerIdentity owner,
        HistoryCursor? cursor, int take, CancellationToken cancellationToken = default);

    Task<HistoryGameSummary?> ReadCompletedSummaryAsync(OwnerIdentity owner, Guid gameId,
        CancellationToken cancellationToken = default);

    Task<HistoryGameDetail?> ReadGameAsync(OwnerIdentity owner, Guid gameId,
        CancellationToken cancellationToken = default);
}
