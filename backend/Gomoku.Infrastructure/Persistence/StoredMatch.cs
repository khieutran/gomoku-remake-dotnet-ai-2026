namespace Gomoku.Infrastructure.Persistence;

public sealed class StoredMatch
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public Guid? GuestSessionId { get; set; }
    public required string RuleVersion { get; set; }
    public int BoardSize { get; set; }
    public int XScore { get; set; }
    public int OScore { get; set; }
    public Guid? CurrentGameId { get; set; }
    public long Revision { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
