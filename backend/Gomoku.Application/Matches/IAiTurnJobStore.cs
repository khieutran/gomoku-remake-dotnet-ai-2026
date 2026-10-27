using Gomoku.Application.Identity;

namespace Gomoku.Application.Matches;

public sealed record AiTurnClaim(Guid JobId, Guid LeaseId, OwnerIdentity Owner,
    Guid MatchId, Guid GameId, long ExpectedRevision, int Attempts);

public sealed record AiTurnProgress(int Attempts, string? LastErrorCode,
    DateTimeOffset NextAttemptAt);

public interface IAiTurnJobStore
{
    Task<AiTurnClaim?> ClaimNextAsync(TimeSpan leaseDuration, CancellationToken cancellationToken);
    Task CompleteAsync(AiTurnClaim claim, CancellationToken cancellationToken);
    Task RetryAsync(AiTurnClaim claim, string errorCode, CancellationToken cancellationToken);
    Task<AiTurnProgress?> ReadProgressAsync(Guid gameId, long expectedRevision,
        CancellationToken cancellationToken);
}
