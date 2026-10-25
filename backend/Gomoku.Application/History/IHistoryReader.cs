using Gomoku.Application.Identity;

namespace Gomoku.Application.History;

public interface IHistoryReader
{
    Task<IReadOnlyList<HistoryGameSummary>> ReadCompletedAsync(OwnerIdentity owner,
        HistoryCursor? cursor, int take, CancellationToken cancellationToken = default);

    Task<HistoryGameDetail?> ReadGameAsync(OwnerIdentity owner, Guid gameId,
        CancellationToken cancellationToken = default);
}
