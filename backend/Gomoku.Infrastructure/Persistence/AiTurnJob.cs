namespace Gomoku.Infrastructure.Persistence;

public sealed class AiTurnJob
{
    public Guid Id { get; set; }
    public Guid MatchId { get; set; }
    public Guid GameId { get; set; }
    public long ExpectedRevision { get; set; }
    public int Attempts { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public string? LastErrorCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
