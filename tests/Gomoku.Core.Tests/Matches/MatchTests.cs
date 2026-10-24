using Gomoku.Core;

namespace Gomoku.Core.Tests;

public sealed class MatchTests
{
    [Fact]
    public void NewMatchStartsWithZeroScoresAndXOpening()
    {
        var match = Match.Start(new RuleSet(20));

        var snapshot = match.Snapshot();
        Assert.Equal(0, snapshot.XScore);
        Assert.Equal(0, snapshot.OScore);
        Assert.Equal(Stone.X, snapshot.OpeningStone);
        Assert.Equal(Stone.X, snapshot.CurrentGame.NextStone);
        Assert.Equal(GameStatus.InProgress, snapshot.CurrentGame.Status);
        Assert.Empty(snapshot.CompletedGames);
    }

    [Fact]
    public void InvalidMoveDoesNotChangeMatchState()
    {
        var match = Match.Start(new RuleSet(20));
        Assert.Equal(MoveResult.Played, match.TryPlay(new Position(10, 10)));

        Assert.Equal(MoveResult.Occupied, match.TryPlay(new Position(10, 10)));
        Assert.Equal(MoveResult.OutOfBounds, match.TryPlay(new Position(-1, 0)));

        var snapshot = match.Snapshot();
        Assert.Equal(0, snapshot.XScore);
        Assert.Equal(0, snapshot.OScore);
        Assert.Equal(Stone.O, snapshot.CurrentGame.NextStone);
        Assert.Single(snapshot.CurrentGame.Moves);
        Assert.Empty(snapshot.CompletedGames);
    }

    [Fact]
    public void CannotStartNextGameBeforeCurrentGameFinishes()
    {
        var match = Match.Start(new RuleSet(20));
        match.TryPlay(new Position(10, 10));

        Assert.False(match.StartNextGame());

        var snapshot = match.Snapshot();
        Assert.Equal(Stone.X, snapshot.OpeningStone);
        Assert.Equal(Stone.O, snapshot.CurrentGame.NextStone);
        Assert.Equal(Stone.X, snapshot.CurrentGame.Board.Get(new Position(10, 10)));
        Assert.Single(snapshot.CurrentGame.Moves);
        Assert.Empty(snapshot.CompletedGames);
    }

    [Fact]
    public void XWinAwardsThreePointsOnlyOnce()
    {
        var match = Match.Start(new RuleSet(20));
        FinishWin(match, Stone.X);

        Assert.Equal(MoveResult.Finished, match.TryPlay(new Position(10, 11)));
        Assert.Equal(MoveResult.Finished, match.TryPlay(new Position(15, 15)));

        var snapshot = match.Snapshot();
        Assert.Equal(3, snapshot.XScore);
        Assert.Equal(0, snapshot.OScore);
        Assert.Equal(GameStatus.XWon, snapshot.CurrentGame.Status);
        Assert.Equal(9, snapshot.CurrentGame.Moves.Count);
        Assert.Equal(Stone.Empty, snapshot.CurrentGame.Board.Get(new Position(15, 15)));
    }

    [Fact]
    public void NextGameKeepsScoreAndCompletedGameAndLetsOOpen()
    {
        var match = Match.Start(new RuleSet(20));
        FinishWin(match, Stone.X);
        var before = match.Snapshot();

        Assert.True(match.StartNextGame());

        var after = match.Snapshot();
        Assert.Equal(3, after.XScore);
        Assert.Equal(0, after.OScore);
        Assert.Equal(Stone.O, after.OpeningStone);
        Assert.Equal(Stone.O, after.CurrentGame.NextStone);
        Assert.Equal(Stone.Empty, after.CurrentGame.Board.Get(new Position(10, 10)));
        Assert.Empty(after.CurrentGame.Moves);
        Assert.Single(after.CompletedGames);
        Assert.Equal(GameStatus.XWon, after.CompletedGames[0].Status);
        Assert.Equal(Stone.X, after.CompletedGames[0].Board.Get(new Position(10, 11)));

        Assert.Equal(3, before.XScore);
        Assert.Empty(before.CompletedGames);
        Assert.Equal(GameStatus.XWon, before.CurrentGame.Status);
        Assert.Equal(Stone.X, before.CurrentGame.Board.Get(new Position(10, 11)));

        Assert.Equal(MoveResult.Played, match.TryPlay(new Position(5, 6)));
        Assert.Equal(Stone.O, match.Snapshot().CurrentGame.Board.Get(new Position(5, 6)));
        Assert.Equal(Stone.Empty, match.Snapshot().CurrentGame.Board.Get(new Position(10, 10)));
    }

    [Fact]
    public void OWinAwardsThreePointsAndNextOpenerIsX()
    {
        var match = Match.Start(new RuleSet(20));
        FinishWin(match, Stone.X);
        Assert.True(match.StartNextGame());
        FinishWin(match, Stone.O);

        var afterSecondWin = match.Snapshot();
        Assert.Equal(3, afterSecondWin.XScore);
        Assert.Equal(3, afterSecondWin.OScore);
        Assert.Equal(GameStatus.OWon, afterSecondWin.CurrentGame.Status);
        Assert.Single(afterSecondWin.CompletedGames);

        Assert.True(match.StartNextGame());
        var thirdGame = match.Snapshot();
        Assert.Equal(Stone.X, thirdGame.OpeningStone);
        Assert.Equal(Stone.X, thirdGame.CurrentGame.NextStone);
        Assert.Equal(3, thirdGame.XScore);
        Assert.Equal(3, thirdGame.OScore);
        Assert.Equal(2, thirdGame.CompletedGames.Count);
        Assert.Equal(GameStatus.XWon, thirdGame.CompletedGames[0].Status);
        Assert.Equal(GameStatus.OWon, thirdGame.CompletedGames[1].Status);
    }

    [Fact]
    public void DrawAwardsOnePointEachAndAlternatesOpeningStone()
    {
        var match = Match.Start(new RuleSet(5));
        FinishDraw(match);

        var finished = match.Snapshot();
        Assert.Equal(GameStatus.Draw, finished.CurrentGame.Status);
        Assert.Equal(1, finished.XScore);
        Assert.Equal(1, finished.OScore);
        Assert.Equal(MoveResult.Finished, match.TryPlay(new Position(2, 2)));
        Assert.Equal(1, match.Snapshot().XScore);
        Assert.Equal(1, match.Snapshot().OScore);

        Assert.True(match.StartNextGame());
        var next = match.Snapshot();
        Assert.Equal(Stone.O, next.OpeningStone);
        Assert.Equal(Stone.O, next.CurrentGame.NextStone);
        Assert.Equal(1, next.XScore);
        Assert.Equal(1, next.OScore);
        Assert.Single(next.CompletedGames);
        Assert.Equal(GameStatus.Draw, next.CompletedGames[0].Status);
    }

    [Fact]
    public void NewMatchStartsFromZeroWithoutChangingPreviousMatch()
    {
        var oldMatch = Match.Start(new RuleSet(20));
        FinishWin(oldMatch, Stone.X);
        Assert.True(oldMatch.StartNextGame());
        oldMatch.TryPlay(new Position(5, 6));

        var newMatch = Match.Start(new RuleSet(20));

        var oldSnapshot = oldMatch.Snapshot();
        Assert.Equal(3, oldSnapshot.XScore);
        Assert.Equal(Stone.O, oldSnapshot.OpeningStone);
        Assert.Single(oldSnapshot.CompletedGames);
        Assert.Single(oldSnapshot.CurrentGame.Moves);
        var newSnapshot = newMatch.Snapshot();
        Assert.Equal(0, newSnapshot.XScore);
        Assert.Equal(0, newSnapshot.OScore);
        Assert.Equal(Stone.X, newSnapshot.OpeningStone);
        Assert.Empty(newSnapshot.CompletedGames);
        Assert.Empty(newSnapshot.CurrentGame.Moves);
    }

    private static void FinishWin(Match match, Stone opener)
    {
        Assert.Equal(opener, match.Snapshot().OpeningStone);
        for (var column = 7; column < 11; column++)
        {
            Assert.Equal(MoveResult.Played, match.TryPlay(new Position(10, column)));
            Assert.Equal(MoveResult.Played, match.TryPlay(new Position(0, column - 7)));
        }

        Assert.Equal(MoveResult.Played, match.TryPlay(new Position(10, 11)));
    }

    private static void FinishDraw(Match match)
    {
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

        for (var index = 0; index < 12; index++)
        {
            Assert.Equal(MoveResult.Played, match.TryPlay(xPositions[index]));
            Assert.Equal(MoveResult.Played, match.TryPlay(oPositions[index]));
        }

        Assert.Equal(MoveResult.Played, match.TryPlay(new Position(2, 2)));
    }
}
