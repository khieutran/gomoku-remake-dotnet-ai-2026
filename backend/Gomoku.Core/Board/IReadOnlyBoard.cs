namespace Gomoku.Core;

public interface IReadOnlyBoard
{
    int Size { get; }
    Stone Get(Position position);
}
