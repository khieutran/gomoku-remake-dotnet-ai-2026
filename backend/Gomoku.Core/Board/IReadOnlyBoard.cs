namespace Gomoku.Core;

public interface IReadOnlyBoard
{
    int Size { get; }
    int Columns { get; }
    Stone Get(Position position);
}
