using Gomoku.Application.Identity;

namespace Gomoku.Application.Matches;

public sealed record CommandRecord(OwnerIdentity Owner, string CommandId, Guid MatchId,
    string RequestHash, int HttpStatus, string ResponseBody, long Revision);

public enum CommitOutcome
{
    Committed,
    StaleRevision,
    DuplicateCommand
}

public interface IGameFlowStore
{
    Task<MatchRecord?> LoadAsync(OwnerIdentity owner, Guid matchId, CancellationToken cancellationToken = default);
    Task<CommandRecord?> FindReceiptAsync(OwnerIdentity owner, string commandId,
        CancellationToken cancellationToken = default);
    Task<CommitOutcome> CreateAsync(MatchRecord match, CommandRecord receipt,
        CancellationToken cancellationToken = default);
    Task<CommitOutcome> CommitAsync(MatchRecord updated, long expectedRevision,
        MoveRecord? addedMove, GameRecord? addedGame, bool completedGame, CommandRecord receipt,
        CancellationToken cancellationToken = default);
}
