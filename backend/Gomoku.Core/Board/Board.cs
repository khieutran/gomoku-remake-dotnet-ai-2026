namespace Gomoku.Core;

public sealed class Board
{
    private readonly Stone[,] _cells;

    public Board(int size, int? columns = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columns ?? size);
        Size = size;
        Columns = columns ?? size;
        _cells = new Stone[size, Columns];
    }

    public int Size { get; }
    public int Columns { get; }

    public Stone Get(Position position)
    {
        if (!Contains(position))
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        return _cells[position.Row, position.Column];
    }

    public bool TryPlace(Position position, Stone stone)
    {
        if (!Contains(position) || stone is not (Stone.X or Stone.O) || _cells[position.Row, position.Column] != Stone.Empty)
        {
            return false;
        }

        _cells[position.Row, position.Column] = stone;
        return true;
    }

    public BoardSnapshot Snapshot() => new(_cells);

    internal bool Contains(Position position) =>
        position.Row >= 0 && position.Row < Size && position.Column >= 0 && position.Column < Columns;
}
