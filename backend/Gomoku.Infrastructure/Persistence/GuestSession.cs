namespace Gomoku.Infrastructure.Persistence;

public sealed class GuestSession
{
    public Guid Id { get; set; }
    public required byte[] TokenHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public long HistoryRevision { get; set; }
}
