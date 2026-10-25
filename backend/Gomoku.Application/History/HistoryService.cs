using Gomoku.Application.Identity;

namespace Gomoku.Application.History;

public sealed class HistoryService(IHistoryReader reader)
{
    public async Task<HistoryPage> ListAsync(OwnerIdentity owner, HistoryCursor? cursor, int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 50);
        var rows = await reader.ReadCompletedAsync(owner, cursor, limit + 1, cancellationToken);
        var items = rows.Take(limit).ToArray();
        var nextCursor = rows.Count > limit
            ? new HistoryCursor(items[^1].FinishedAt, items[^1].GameId).Encode()
            : null;
        return new HistoryPage(items, nextCursor);
    }

    public Task<HistoryGameDetail?> GetAsync(OwnerIdentity owner, Guid gameId,
        CancellationToken cancellationToken = default) =>
        reader.ReadGameAsync(owner, gameId, cancellationToken);
}
