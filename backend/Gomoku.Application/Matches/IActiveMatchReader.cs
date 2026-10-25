using Gomoku.Application.Identity;

namespace Gomoku.Application.Matches;

public sealed record ActiveMatchSummary(Guid MatchId, Guid GameId, long Revision,
    DateTimeOffset UpdatedAt);
public sealed record ActiveMatchPage(IReadOnlyList<ActiveMatchSummary> Items, int? NextOffset);

public interface IActiveMatchReader
{
    Task<IReadOnlyList<ActiveMatchSummary>> ReadAsync(OwnerIdentity owner, int offset, int take,
        CancellationToken cancellationToken = default);
}
