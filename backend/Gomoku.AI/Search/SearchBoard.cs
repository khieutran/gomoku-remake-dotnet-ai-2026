using Gomoku.Core;

namespace Gomoku.AI;

internal sealed class SearchBoard : IReadOnlyBoard
{
    private readonly Stone[,] _cells;

    public SearchBoard(BoardSnapshot snapshot)
    {
        Size = snapshot.Size;
        _cells = new Stone[Size, Size];
        for (var row = 0; row < Size; row++)
        for (var column = 0; column < Size; column++)
        {
            var stone = snapshot.Get(new Position(row, column));
            _cells[row, column] = stone;
            if (stone != Stone.Empty) OccupiedCount++;
        }
    }

    public int Size { get; }
    public int OccupiedCount { get; private set; }

    public Stone Get(Position position)
    {
        if (!Contains(position)) throw new ArgumentOutOfRangeException(nameof(position));
        return _cells[position.Row, position.Column];
    }

    public void Play(Position position, Stone stone)
    {
        if (!Contains(position) || stone is not (Stone.X or Stone.O) || Get(position) != Stone.Empty)
            throw new InvalidOperationException("Search move is not legal.");
        _cells[position.Row, position.Column] = stone;
        OccupiedCount++;
    }

    public void Undo(Position position)
    {
        if (!Contains(position) || Get(position) == Stone.Empty)
            throw new InvalidOperationException("Search move cannot be undone.");
        _cells[position.Row, position.Column] = Stone.Empty;
        OccupiedCount--;
    }

    private bool Contains(Position position) => position.Row >= 0 && position.Row < Size &&
        position.Column >= 0 && position.Column < Size;
}
