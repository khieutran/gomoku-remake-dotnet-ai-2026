using Gomoku.AI;
using Gomoku.Core;

namespace Gomoku.AI.Tests;

public sealed class SeededLegalMoveSelectorTests
{
    private static readonly RuleSet Rules = new(20);

    [Fact]
    public void EmptyBoardChoosesCenterAndDoesNotChangeSnapshot()
    {
        var game = Game.Start(Rules, Stone.O);
        var snapshot = game.Snapshot();

        var move = new SeededLegalMoveSelector(42).SelectMove(snapshot, Stone.O,
            TimeSpan.Zero, CancellationToken.None);

        Assert.Equal(new Position(10, 10), move);
        Assert.Equal(Stone.Empty, snapshot.Board.Get(new Position(10, 10)));
        Assert.Equal(0, snapshot.MoveIndex);
    }

    [Fact]
    public void MidgameSelectsEmptyNeighborWithoutChangingGame()
    {
        var game = Game.Start(Rules, Stone.O);
        Assert.Equal(MoveResult.Played, game.TryPlay(new Position(10, 10)));
        Assert.Equal(MoveResult.Played, game.TryPlay(new Position(0, 0)));
        var snapshot = game.Snapshot();

        var move = new SeededLegalMoveSelector(7).SelectMove(snapshot, Stone.O,
            TimeSpan.FromMilliseconds(1), CancellationToken.None);

        Assert.NotNull(move);
        Assert.Equal(Stone.Empty, snapshot.Board.Get(move.Value));
        Assert.InRange(move.Value.Row, 9, 11);
        Assert.InRange(move.Value.Column, 9, 11);
        Assert.Equal(2, game.Snapshot().MoveIndex);
        Assert.Equal(Stone.O, game.Snapshot().NextStone);
    }

    [Fact]
    public void SameSeedAndPositionReturnSameMove()
    {
        var game = Game.Start(Rules, Stone.O);
        game.TryPlay(new Position(10, 10));
        game.TryPlay(new Position(0, 0));

        var first = new SeededLegalMoveSelector(17).SelectMove(game.Snapshot(), Stone.O,
            TimeSpan.FromSeconds(1), CancellationToken.None);
        var second = new SeededLegalMoveSelector(17).SelectMove(game.Snapshot(), Stone.O,
            TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.Equal(first, second);
    }

    [Fact]
    public void FinishedGameHasNoMove()
    {
        var game = Game.Start(Rules, Stone.X);
        for (var column = 5; column < 9; column++)
        {
            game.TryPlay(new Position(10, column));
            game.TryPlay(new Position(0, column));
        }
        game.TryPlay(new Position(10, 9));
        Assert.Equal(GameStatus.XWon, game.Snapshot().Status);

        Assert.Null(new SeededLegalMoveSelector(1).SelectMove(game.Snapshot(), Stone.O,
            TimeSpan.FromSeconds(1), CancellationToken.None));
    }

    [Fact]
    public void WrongSideIsRejected()
    {
        var snapshot = Game.Start(Rules, Stone.X).Snapshot();

        Assert.Throws<ArgumentException>(() => new SeededLegalMoveSelector(1).SelectMove(
            snapshot, Stone.O, TimeSpan.FromSeconds(1), CancellationToken.None));
    }

    [Fact]
    public void CancellationIsObservedBeforeChoosingMove()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.Throws<OperationCanceledException>(() => new SeededLegalMoveSelector(1).SelectMove(
            Game.Start(Rules, Stone.O).Snapshot(), Stone.O, TimeSpan.Zero, source.Token));
    }

    [Fact]
    public void NegativeBudgetIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SeededLegalMoveSelector(1).SelectMove(
            Game.Start(Rules, Stone.O).Snapshot(), Stone.O, TimeSpan.FromMilliseconds(-1),
            CancellationToken.None));
    }
}
