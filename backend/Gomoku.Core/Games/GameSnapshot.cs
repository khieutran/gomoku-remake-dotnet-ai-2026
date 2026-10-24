namespace Gomoku.Core;

public sealed class GameSnapshot
{
    internal GameSnapshot(BoardSnapshot board, RuleSet rules, GameStatus status, Stone? nextStone,
        IReadOnlyList<Move> moves)
    {
        Board = board;
        Rules = rules;
        Status = status;
        NextStone = nextStone;
        Moves = moves;
    }

    public BoardSnapshot Board { get; }
    public RuleSet Rules { get; }
    public GameStatus Status { get; }
    public Stone? NextStone { get; }
    public IReadOnlyList<Move> Moves { get; }
    public int MoveIndex => Moves.Count;
}
