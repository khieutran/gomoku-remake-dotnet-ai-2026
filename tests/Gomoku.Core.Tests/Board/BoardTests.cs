using Gomoku.Core;

namespace Gomoku.Core.Tests;

public sealed class BoardTests
{
    [Fact]
    public void RectangularBoardHasThirtyPlayableColumnsAndTwentyRows()
    {
        var board = new Board(20, 30);
        Assert.Equal(20, board.Size);
        Assert.Equal(30, board.Columns);
        Assert.True(board.TryPlace(new Position(19, 29), Stone.X));
        Assert.False(board.TryPlace(new Position(20, 0), Stone.X));
        Assert.False(board.TryPlace(new Position(0, 30), Stone.X));
        Assert.Equal(30, board.Snapshot().Columns);
    }

    [Fact]
    public void FiveStonesAtRightEdgeWinOnRectangularBoard()
    {
        var board = new Board(20, 30);
        for (var column = 25; column < 30; column++)
            Assert.True(board.TryPlace(new Position(7, column), Stone.X));

        Assert.Equal(GameStatus.XWon, RuleEvaluator.Evaluate(board.Snapshot(),
            new Position(7, 29), Stone.X, new RuleSet(20, 30)));
    }

    [Fact]
    public void NewBoardHasTwentyEmptyRowsAndColumns()
    {
        var board = new Board(20);

        Assert.Equal(20, board.Size);
        for (var row = 0; row < board.Size; row++)
        {
            for (var column = 0; column < board.Size; column++)
            {
                Assert.Equal(Stone.Empty, board.Get(new Position(row, column)));
            }
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 19)]
    [InlineData(19, 0)]
    [InlineData(19, 19)]
    public void PlacesStoneOnBoardEdges(int row, int column)
    {
        var board = new Board(20);
        var position = new Position(row, column);

        Assert.True(board.TryPlace(position, Stone.X));
        Assert.Equal(Stone.X, board.Get(position));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(20, 0)]
    [InlineData(0, 20)]
    public void RejectsPositionsOutsideBoard(int row, int column)
    {
        var board = new Board(20);
        var position = new Position(row, column);

        Assert.False(board.TryPlace(position, Stone.X));
        Assert.Throws<ArgumentOutOfRangeException>(() => board.Get(position));
        Assert.Equal(Stone.Empty, board.Get(new Position(0, 0)));
    }

    [Fact]
    public void RejectsEmptyAndOccupiedCellsWithoutChangingExistingStone()
    {
        var board = new Board(20);
        var position = new Position(5, 6);

        Assert.False(board.TryPlace(position, Stone.Empty));
        Assert.Equal(Stone.Empty, board.Get(position));
        Assert.True(board.TryPlace(position, Stone.X));
        Assert.False(board.TryPlace(position, Stone.O));
        Assert.Equal(Stone.X, board.Get(position));
    }

    [Fact]
    public void SnapshotDoesNotChangeAfterBoardIsUpdated()
    {
        var board = new Board(20);
        var position = new Position(5, 6);
        var before = board.Snapshot();

        Assert.True(board.TryPlace(position, Stone.O));
        var after = board.Snapshot();

        Assert.Equal(20, before.Size);
        Assert.Equal(Stone.Empty, before.Get(position));
        Assert.Equal(Stone.O, after.Get(position));
        Assert.Equal(Stone.O, board.Get(position));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsNonPositiveBoardSize(int size)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Board(size));
    }
}
