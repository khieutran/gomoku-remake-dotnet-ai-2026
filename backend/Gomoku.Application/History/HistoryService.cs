using Gomoku.Application.Identity;

namespace Gomoku.Application.History;

public sealed class HistoryService(IHistoryReader reader, IHistoryCache cache)
{
    private static readonly TimeSpan PageTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan GameTtl = TimeSpan.FromHours(1);

    public async Task<HistoryPage> ListAsync(OwnerIdentity owner, HistoryCursor? cursor, int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 50);
        var revision = await reader.ReadRevisionAsync(owner, cancellationToken);
        var key = $"gomoku:v1:history:page:{owner.Scope}:{owner.Id:N}:r{revision}:l{limit}:c{cursor?.Encode() ?? "first"}";
        var cached = await cache.GetPageAsync(key, cancellationToken);
        if (cached is not null) return cached;

        var rows = await reader.ReadCompletedAsync(owner, cursor, limit + 1, cancellationToken);
        var items = rows.Take(limit).ToArray();
        var nextCursor = rows.Count > limit
            ? new HistoryCursor(items[^1].FinishedAt, items[^1].GameId).Encode()
            : null;
        var page = new HistoryPage(items, nextCursor);
        await cache.SetPageAsync(key, page, PageTtl, cancellationToken);
        return page;
    }

    public async Task<HistoryGameDetail?> GetAsync(OwnerIdentity owner, Guid gameId,
        CancellationToken cancellationToken = default)
    {
        var summary = await reader.ReadCompletedSummaryAsync(owner, gameId, cancellationToken);
        if (summary is null) return null;
        var key = $"gomoku:v1:history:game:{owner.Scope}:{owner.Id:N}:{gameId:N}:" +
            $"{summary.RuleVersion}:{summary.FinishedAt.ToUniversalTime().Ticks}:{summary.MoveCount}";
        var cached = await cache.GetGameAsync(key, cancellationToken);
        if (cached is not null && cached.GameId == gameId && cached.MatchId == summary.MatchId)
            return cached;

        var game = await reader.ReadGameAsync(owner, gameId, cancellationToken);
        if (game is not null) await cache.SetGameAsync(key, game, GameTtl, cancellationToken);
        return game;
    }
}
