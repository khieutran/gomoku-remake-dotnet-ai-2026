using Gomoku.Core;

namespace Gomoku.Infrastructure.Persistence;

public enum StoredGameState
{
    Active,
    Finished,
    Abandoned
}

public sealed class StoredGame
{
    public Guid Id { get; set; }
    public Guid MatchId { get; set; }
    public int Number { get; set; }
    public Stone OpeningStone { get; set; }
    public GameStatus RuleStatus { get; set; }
    public StoredGameState State { get; set; }
    public string? AiVersion { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}
