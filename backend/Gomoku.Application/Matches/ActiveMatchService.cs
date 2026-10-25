using Gomoku.Application.Identity;

namespace Gomoku.Application.Matches;

public sealed class ActiveMatchService(IActiveMatchReader reader)
{
    public async Task<ActiveMatchPage> ListAsync(OwnerIdentity owner, ActiveMatchCursor? cursor, int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 50);
        if (cursor is { } point && (point.Scope != owner.Scope || point.OwnerId != owner.Id))
            throw new ArgumentException("Cursor owner does not match.", nameof(cursor));
        var rows = await reader.ReadAsync(owner, cursor, limit + 1, cancellationToken);
        var items = rows.Take(limit).ToArray();
        var nextCursor = rows.Count > limit
            ? new ActiveMatchCursor(owner.Scope, owner.Id, items[^1].CreatedAt, items[^1].MatchId).Encode()
            : null;
        return new ActiveMatchPage(items, nextCursor);
    }
}
