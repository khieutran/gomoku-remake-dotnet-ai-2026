using Gomoku.Core;

namespace Gomoku.Core.Tests;

public sealed class RuleEvaluatorTests
{
    [Theory]
    [InlineData(0, 1, Stone.X, GameStatus.XWon)]
    [InlineData(1, 0, Stone.X, GameStatus.XWon)]
    [InlineData(1, 1, Stone.X, GameStatus.XWon)]
    [InlineData(1, -1, Stone.X, GameStatus.XWon)]
    [InlineData(0, 1, Stone.O, GameStatus.OWon)]
    [InlineData(1, 0, Stone.O, GameStatus.OWon)]
    [InlineData(1, 1, Stone.O, GameStatus.OWon)]
    [InlineData(1, -1, Stone.O, GameStatus.OWon)]
    public void ExactlyFiveWinsInEveryDirection(int rowStep, int columnStep, Stone mover, GameStatus expected)
    {
        var board = new Board(20);
        var start = new Position(8, 8);
        for (var index = 0; index < 5; index++)
        {
            board.TryPlace(new Position(start.Row + index * rowStep, start.Column + index * columnStep), mover);
        }

        Assert.Equal(expected, RuleEvaluator.Evaluate(board.Snapshot(),
            new Position(start.Row + 2 * rowStep, start.Column + 2 * columnStep), mover, new RuleSet(20)));
    }

    [Fact]
    public void FourStonesDoNotWin()
    {
        var board = new Board(20);
        PlaceRow(board, 10, 7, 10, Stone.X);

        Assert.Equal(GameStatus.InProgress,
            RuleEvaluator.Evaluate(board.Snapshot(), new Position(10, 10), Stone.X, new RuleSet(20)));
    }

    [Fact]
    public void SixStonesCompletedByMiddleMoveDoNotWin()
    {
        var board = new Board(20);
        PlaceRow(board, 10, 7, 12, Stone.X);

        Assert.Equal(GameStatus.InProgress,
            RuleEvaluator.Evaluate(board.Snapshot(), new Position(10, 9), Stone.X, new RuleSet(20)));
    }

    [Theory]
    [InlineData(Stone.X, Stone.O)]
    [InlineData(Stone.O, Stone.X)]
    public void FiveStonesBlockedImmediatelyAtBothEndsDoNotWin(Stone mover, Stone opponent)
    {
        var board = new Board(20);
        PlaceRow(board, 10, 7, 11, mover);
        board.TryPlace(new Position(10, 6), opponent);
        board.TryPlace(new Position(10, 12), opponent);

        Assert.Equal(GameStatus.InProgress,
            RuleEvaluator.Evaluate(board.Snapshot(), new Position(10, 9), mover, new RuleSet(20)));
    }

    [Fact]
    public void FiveStonesWithOneOpenEndWin()
    {
        var board = new Board(20);
        PlaceRow(board, 10, 7, 11, Stone.X);
        board.TryPlace(new Position(10, 6), Stone.O);

        Assert.Equal(GameStatus.XWon,
            RuleEvaluator.Evaluate(board.Snapshot(), new Position(10, 11), Stone.X, new RuleSet(20)));
    }

    [Fact]
    public void OpponentStoneOneCellAwayDoesNotBlock()
    {
        var board = new Board(20);
        PlaceRow(board, 10, 7, 11, Stone.X);
        board.TryPlace(new Position(10, 5), Stone.O);
        board.TryPlace(new Position(10, 13), Stone.O);

        Assert.Equal(GameStatus.XWon,
            RuleEvaluator.Evaluate(board.Snapshot(), new Position(10, 11), Stone.X, new RuleSet(20)));
    }

    [Theory]
    [InlineData(0, 4, 5)]
    [InlineData(15, 19, 14)]
    public void BoardEdgeDoesNotCountAsBlockedEnd(int first, int last, int blocker)
    {
        var board = new Board(20);
        PlaceRow(board, 10, first, last, Stone.X);
        board.TryPlace(new Position(10, blocker), Stone.O);

        Assert.Equal(GameStatus.XWon,
            RuleEvaluator.Evaluate(board.Snapshot(), new Position(10, first), Stone.X, new RuleSet(20)));
    }

    [Fact]
    public void DiagonalFromCornerWinsEvenWithOpponentAtOtherEnd()
    {
        var board = new Board(20);
        for (var coordinate = 0; coordinate < 5; coordinate++)
        {
            board.TryPlace(new Position(coordinate, coordinate), Stone.X);
        }

        board.TryPlace(new Position(5, 5), Stone.O);

        Assert.Equal(GameStatus.XWon,
            RuleEvaluator.Evaluate(board.Snapshot(), new Position(4, 4), Stone.X, new RuleSet(20)));
    }

    [Fact]
    public void UnblockedVerticalWinCountsWhenHorizontalFiveIsBlocked()
    {
        var board = new Board(20);
        PlaceRow(board, 10, 8, 12, Stone.X);
        for (var row = 8; row <= 12; row++)
        {
            board.TryPlace(new Position(row, 10), Stone.X);
        }

        board.TryPlace(new Position(10, 7), Stone.O);
        board.TryPlace(new Position(10, 13), Stone.O);

        Assert.Equal(GameStatus.XWon,
            RuleEvaluator.Evaluate(board.Snapshot(), new Position(10, 10), Stone.X, new RuleSet(20)));
    }

    [Fact]
    public void FullBoardWithoutWinIsDraw()
    {
        var board = new Board(5);
        var rows = new[] { "OXOXO", "XOXOX", "OXXXO", "XOXOX", "OXOXO" };
        for (var row = 0; row < 5; row++)
        {
            for (var column = 0; column < 5; column++)
            {
                board.TryPlace(new Position(row, column), rows[row][column] == 'X' ? Stone.X : Stone.O);
            }
        }

        Assert.Equal(GameStatus.Draw,
            RuleEvaluator.Evaluate(board.Snapshot(), new Position(2, 2), Stone.X, new RuleSet(5)));
    }

    [Fact]
    public void InvalidLastMoveIsRejected()
    {
        var board = new Board(20);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RuleEvaluator.Evaluate(board.Snapshot(), new Position(-1, 0), Stone.X, new RuleSet(20)));
        Assert.Throws<ArgumentException>(() =>
            RuleEvaluator.Evaluate(board.Snapshot(), new Position(10, 10), Stone.X, new RuleSet(20)));
    }

    private static void PlaceRow(Board board, int row, int first, int last, Stone stone)
    {
        for (var column = first; column <= last; column++)
        {
            board.TryPlace(new Position(row, column), stone);
        }
    }
}
