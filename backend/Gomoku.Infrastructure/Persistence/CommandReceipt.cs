namespace Gomoku.Infrastructure.Persistence;

public sealed class CommandReceipt
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public Guid? GuestSessionId { get; set; }
    public required string CommandId { get; set; }
    public Guid? MatchId { get; set; }
    public required string RequestHash { get; set; }
    public int HttpStatus { get; set; }
    public required string ResponseBody { get; set; }
    public long RevisionAfterCommit { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
