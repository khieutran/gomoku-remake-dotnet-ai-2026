using Gomoku.Core;

namespace Gomoku.Infrastructure.Persistence;

public enum MoveActor
{
    Human,
    AI
}

public sealed class StoredMove
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public int Step { get; set; }
    public int Row { get; set; }
    public int Column { get; set; }
    public Stone Stone { get; set; }
    public MoveActor Actor { get; set; }
    public string? ActualAiVersion { get; set; }
    public string? AiFallbackReason { get; set; }
    public DateTimeOffset PlayedAt { get; set; }
}
