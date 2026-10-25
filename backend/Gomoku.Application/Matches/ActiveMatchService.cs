using Gomoku.Application.Identity;

namespace Gomoku.Application.Matches;

public sealed class ActiveMatchService(IActiveMatchReader reader)
{
    public async Task<ActiveMatchPage> ListAsync(OwnerIdentity owner, int offset, int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 50);
        var rows = await reader.ReadAsync(owner, offset, limit + 1, cancellationToken);
        return new ActiveMatchPage(rows.Take(limit).ToArray(),
            rows.Count > limit ? offset + limit : null);
    }
}
