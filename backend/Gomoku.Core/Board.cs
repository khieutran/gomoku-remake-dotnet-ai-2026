namespace Gomoku.Core;

public sealed class Board
{
    private readonly Stone[,] _cells;

    public Board(int size)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        Size = size;
        _cells = new Stone[size, size];
    }

    public int Size { get; }

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

    private bool Contains(Position position) =>
        position.Row >= 0 && position.Row < Size && position.Column >= 0 && position.Column < Size;
}
