namespace Gomoku.Core;

public sealed class GameSnapshot
{
    internal GameSnapshot(BoardSnapshot board, GameStatus status, Stone? nextStone, IReadOnlyList<Move> moves)
    {
        Board = board;
        Status = status;
        NextStone = nextStone;
        Moves = moves;
    }

    public BoardSnapshot Board { get; }
    public GameStatus Status { get; }
    public Stone? NextStone { get; }
    public IReadOnlyList<Move> Moves { get; }
}
