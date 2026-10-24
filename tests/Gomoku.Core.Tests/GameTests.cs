using Gomoku.Core;

namespace Gomoku.Core.Tests;

public sealed class GameTests
{
    [Theory]
    [InlineData(Stone.X)]
    [InlineData(Stone.O)]
    public void NewGameStartsWithChosenOpeningStone(Stone openingStone)
    {
        var game = Game.Start(new RuleSet(20), openingStone);

        var snapshot = game.Snapshot();
        Assert.Equal(GameStatus.InProgress, snapshot.Status);
        Assert.Equal(openingStone, snapshot.NextStone);
        Assert.Equal(20, snapshot.Board.Size);
        Assert.Empty(snapshot.Moves);
        Assert.Equal(Stone.Empty, snapshot.Board.Get(new Position(10, 10)));
    }

    [Fact]
    public void ValidMovesPlaceStonesAndAlternateTurns()
    {
        var game = Game.Start(new RuleSet(20), Stone.X);

        Assert.Equal(MoveResult.Played, game.TryPlay(new Position(10, 10)));
        Assert.Equal(Stone.O, game.Snapshot().NextStone);
        Assert.Equal(MoveResult.Played, game.TryPlay(new Position(10, 11)));

        var snapshot = game.Snapshot();
        Assert.Equal(Stone.X, snapshot.NextStone);
        Assert.Equal(GameStatus.InProgress, snapshot.Status);
        Assert.Equal(Stone.X, snapshot.Board.Get(new Position(10, 10)));
        Assert.Equal(Stone.O, snapshot.Board.Get(new Position(10, 11)));
        Assert.Equal(new Move(1, new Position(10, 10), Stone.X), snapshot.Moves[0]);
        Assert.Equal(new Move(2, new Position(10, 11), Stone.O), snapshot.Moves[1]);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(20, 0)]
    [InlineData(0, 20)]
    public void OutOfBoundsMoveLeavesStateUntouched(int row, int column)
    {
        var game = Game.Start(new RuleSet(20), Stone.X);

        Assert.Equal(MoveResult.OutOfBounds, game.TryPlay(new Position(row, column)));

        var snapshot = game.Snapshot();
        Assert.Equal(Stone.X, snapshot.NextStone);
        Assert.Equal(GameStatus.InProgress, snapshot.Status);
        Assert.Empty(snapshot.Moves);
        Assert.Equal(Stone.Empty, snapshot.Board.Get(new Position(0, 0)));
    }

    [Fact]
    public void OccupiedMoveLeavesBoardTurnAndHistoryUntouched()
    {
        var game = Game.Start(new RuleSet(20), Stone.X);
        game.TryPlay(new Position(10, 10));

        Assert.Equal(MoveResult.Occupied, game.TryPlay(new Position(10, 10)));

        var snapshot = game.Snapshot();
        Assert.Equal(Stone.X, snapshot.Board.Get(new Position(10, 10)));
        Assert.Equal(Stone.O, snapshot.NextStone);
        Assert.Single(snapshot.Moves);
        Assert.Equal(new Move(1, new Position(10, 10), Stone.X), snapshot.Moves[0]);
    }

    [Theory]
    [InlineData(Stone.X, GameStatus.XWon)]
    [InlineData(Stone.O, GameStatus.OWon)]
    public void WinningMoveEndsGameAndRejectsFurtherMoves(Stone openingStone, GameStatus expectedStatus)
    {
        var game = Game.Start(new RuleSet(20), openingStone);
        var expectedOtherStone = openingStone == Stone.X ? Stone.O : Stone.X;
        for (var column = 7; column < 11; column++)
        {
            Assert.Equal(MoveResult.Played, game.TryPlay(new Position(10, column)));
            Assert.Equal(MoveResult.Played, game.TryPlay(new Position(0, column - 7)));
        }

        Assert.Equal(MoveResult.Played, game.TryPlay(new Position(10, 11)));
        var finished = game.Snapshot();
        Assert.Equal(expectedStatus, finished.Status);
        Assert.Null(finished.NextStone);
        Assert.Equal(openingStone, finished.Board.Get(new Position(10, 11)));
        Assert.Equal(expectedOtherStone, finished.Board.Get(new Position(0, 3)));
        Assert.Equal(9, finished.Moves.Count);

        Assert.Equal(MoveResult.Finished, game.TryPlay(new Position(10, 11)));
        Assert.Equal(MoveResult.Finished, game.TryPlay(new Position(-1, 0)));
        Assert.Equal(MoveResult.Finished, game.TryPlay(new Position(15, 15)));
        var after = game.Snapshot();
        Assert.Equal(9, after.Moves.Count);
        Assert.Equal(Stone.Empty, after.Board.Get(new Position(15, 15)));
        Assert.Equal(expectedStatus, after.Status);
    }

    [Fact]
    public void FullBoardWithoutWinnerEndsGameAsDraw()
    {
        var game = Game.Start(new RuleSet(5), Stone.X);
        var rows = new[] { "OXOXO", "XOXOX", "OXXXO", "XOXOX", "OXOXO" };
        var xPositions = new List<Position>();
        var oPositions = new List<Position>();
        for (var row = 0; row < 5; row++)
        {
            for (var column = 0; column < 5; column++)
            {
                var position = new Position(row, column);
                if (position == new Position(2, 2)) continue;
                (rows[row][column] == 'X' ? xPositions : oPositions).Add(position);
            }
        }

        Assert.Equal(12, xPositions.Count);
        Assert.Equal(12, oPositions.Count);
        for (var index = 0; index < 12; index++)
        {
            Assert.Equal(MoveResult.Played, game.TryPlay(xPositions[index]));
            Assert.Equal(MoveResult.Played, game.TryPlay(oPositions[index]));
        }

        Assert.Equal(GameStatus.InProgress, game.Snapshot().Status);
        Assert.Equal(MoveResult.Played, game.TryPlay(new Position(2, 2)));
        var finished = game.Snapshot();
        Assert.Equal(GameStatus.Draw, finished.Status);
        Assert.Null(finished.NextStone);
        Assert.Equal(25, finished.Moves.Count);
        Assert.Equal(MoveResult.Finished, game.TryPlay(new Position(2, 2)));
    }

    [Fact]
    public void SnapshotDoesNotChangeAfterLaterMove()
    {
        var game = Game.Start(new RuleSet(20), Stone.X);
        var before = game.Snapshot();

        game.TryPlay(new Position(10, 10));
        var after = game.Snapshot();

        Assert.Empty(before.Moves);
        Assert.Equal(Stone.X, before.NextStone);
        Assert.Equal(Stone.Empty, before.Board.Get(new Position(10, 10)));
        Assert.Single(after.Moves);
        Assert.Equal(Stone.O, after.NextStone);
        Assert.Equal(Stone.X, after.Board.Get(new Position(10, 10)));
    }

    [Fact]
    public void EmptyStoneCannotOpenGame()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Game.Start(new RuleSet(20), Stone.Empty));
    }
}
