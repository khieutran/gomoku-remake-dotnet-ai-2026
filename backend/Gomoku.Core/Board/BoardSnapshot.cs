namespace Gomoku.Core;

public sealed class BoardSnapshot : IReadOnlyBoard
{
    private readonly Stone[,] _cells;

    internal BoardSnapshot(Stone[,] cells)
    {
        _cells = (Stone[,])cells.Clone();
    }

    public int Size => _cells.GetLength(0);
    public int Columns => _cells.GetLength(1);

    public Stone Get(Position position)
    {
        if (position.Row < 0 || position.Row >= Size || position.Column < 0 || position.Column >= Columns)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        return _cells[position.Row, position.Column];
    }
}
