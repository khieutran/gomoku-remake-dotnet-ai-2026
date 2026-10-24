namespace Gomoku.Core;

public sealed class BoardSnapshot
{
    private readonly Stone[,] _cells;

    internal BoardSnapshot(Stone[,] cells)
    {
        _cells = (Stone[,])cells.Clone();
    }

    public int Size => _cells.GetLength(0);

    public Stone Get(Position position)
    {
        if (position.Row < 0 || position.Row >= Size || position.Column < 0 || position.Column >= Size)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        return _cells[position.Row, position.Column];
    }
}
