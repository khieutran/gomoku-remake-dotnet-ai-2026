using Gomoku.Core;

namespace Gomoku.AI.Tests;

public sealed class SearchMoveSelectorTests
{
    private static readonly RuleSet Rules = new(20);

    [Fact]
    public void SearchOpensNearCenterOfRectangularBoard()
    {
        var game = Game.Start(new RuleSet(20, 30), Stone.O);
        var result = new SearchMoveSelector(2026).Analyze(game.Snapshot(), Stone.O,
            TimeSpan.FromSeconds(1), CancellationToken.None);
        Assert.Equal(new Position(10, 15), result.Move);
    }

    [Fact]
    public void ChoosesImmediateWinBeforeOtherMoves()
    {
        var board = Position(
            x: [(10, 6), (0, 0), (0, 2), (0, 4)],
            o: [(10, 7), (10, 8), (10, 9), (10, 10)]);

        var result = new SearchMoveSelector(2026).Analyze(board, Rules, Stone.O,
            TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.Equal(new Position(10, 11), result.Move);
        Assert.Equal(SearchDecisionReason.ImmediateWin, result.Reason);
    }

    [Fact]
    public void BlocksOnlyImmediateHumanWin()
    {
        var board = Position(
            x: [(10, 7), (10, 8), (10, 9), (10, 10)],
            o: [(10, 6), (0, 0), (0, 2), (0, 4)]);

        var result = new SearchMoveSelector(2026).Analyze(board, Rules, Stone.O,
            TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.Equal(new Position(10, 11), result.Move);
        Assert.Equal(SearchDecisionReason.ImmediateBlock, result.Reason);
    }

    [Fact]
    public void BlocksOpenHumanPairBeforeItBecomesAnOpenThree()
    {
        var board = Position(
            x: [(10, 10), (10, 11)],
            o: [(4, 4), (4, 5)]);

        var result = new SearchMoveSelector(2026).Analyze(board, Rules, Stone.O,
            TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.True(result.Move is { } move &&
            (move == new Position(10, 9) || move == new Position(10, 12)));
    }

    [Fact]
    public void BuildsOpenFourWhenAttackOutweighsHumanPair()
    {
        var board = Position(
            x: [(5, 5), (5, 6), (0, 0)],
            o: [(10, 10), (10, 11), (10, 12)]);

        var result = new SearchMoveSelector(2026).Analyze(board, Rules, Stone.O,
            TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.True(result.Move is { } move &&
            (move == new Position(10, 9) || move == new Position(10, 13)));
        Assert.Equal(SearchDecisionReason.ThreatAttack, result.Reason);
    }

    [Fact]
    public void DefendsOpenHumanThreeWhenOwnAttackIsWeaker()
    {
        var board = Position(
            x: [(5, 5), (5, 6), (5, 7)],
            o: [(10, 10), (10, 11), (0, 0)]);

        var result = new SearchMoveSelector(2026).Analyze(board, Rules, Stone.O,
            TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.True(result.Move is { } move &&
            (move == new Position(5, 4) || move == new Position(5, 8)));
        Assert.Equal(SearchDecisionReason.ThreatBlock, result.Reason);
    }

    [Fact]
    public void EdgeIsNotAnOpponentBlocker()
    {
        var board = Position(
            x: [(10, 5), (0, 0), (0, 2), (0, 4)],
            o: [(10, 0), (10, 1), (10, 2), (10, 3)]);

        var result = new SearchMoveSelector(2026).Analyze(board, Rules, Stone.O,
            TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.Equal(new Position(10, 4), result.Move);
        Assert.Equal(SearchDecisionReason.ImmediateWin, result.Reason);
    }

    [Fact]
    public void BlockedFiveAndOverlineAreNotClassifiedAsImmediateWins()
    {
        var blocked = Position(
            x: [(10, 6), (10, 12), (0, 0), (0, 2)],
            o: [(10, 7), (10, 8), (10, 9), (10, 10)]);
        var overline = Position(
            x: [(0, 0), (0, 2), (0, 4), (0, 6), (0, 8)],
            o: [(10, 7), (10, 8), (10, 9), (10, 11), (10, 12)]);
        var selector = new SearchMoveSelector(2026);

        var blockedResult = selector.Analyze(blocked, Rules, Stone.O,
            TimeSpan.FromMilliseconds(500), CancellationToken.None);
        var overlineResult = selector.Analyze(overline, Rules, Stone.O,
            TimeSpan.FromMilliseconds(500), CancellationToken.None);

        Assert.NotEqual(SearchDecisionReason.ImmediateWin, blockedResult.Reason);
        Assert.NotEqual(SearchDecisionReason.ImmediateWin, overlineResult.Reason);
        Assert.Equal(Stone.Empty, blocked.Get(blockedResult.Move!.Value));
        Assert.Equal(Stone.Empty, overline.Get(overlineResult.Move!.Value));
        Assert.Equal(Stone.Empty, blocked.Get(new Position(10, 11)));
        Assert.Equal(Stone.Empty, overline.Get(new Position(10, 10)));
    }

    [Fact]
    public void SupportsEitherStoneWithTheSameCoreRule()
    {
        var board = Position(
            x: [(8, 7), (8, 8), (8, 9), (8, 10)],
            o: [(8, 6), (0, 0), (0, 2), (0, 4)]);

        var result = new SearchMoveSelector(2026).Analyze(board, Rules, Stone.X,
            TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.Equal(new Position(8, 11), result.Move);
        Assert.Equal(SearchDecisionReason.ImmediateWin, result.Reason);
    }

    [Fact]
    public void ZeroBudgetStillReturnsLegalFallbackAndDoesNotChangeSnapshot()
    {
        var game = Game.Start(Rules, Stone.X);
        game.TryPlay(new Position(10, 10));
        var snapshot = game.Snapshot();

        var result = new SearchMoveSelector(7).Analyze(snapshot.Board, Rules, Stone.O,
            TimeSpan.Zero, CancellationToken.None);

        Assert.NotNull(result.Move);
        Assert.Equal(SearchDecisionReason.Fallback, result.Reason);
        Assert.Equal(Stone.Empty, snapshot.Board.Get(result.Move.Value));
        Assert.Equal(1, game.Snapshot().MoveIndex);
    }

    [Fact]
    public void FinishedGameReturnsNoMoveAndCancellationIsObserved()
    {
        var game = Game.Start(Rules, Stone.X);
        for (var column = 0; column < 4; column++)
        {
            game.TryPlay(new Position(10, column));
            game.TryPlay(new Position(0, column));
        }
        game.TryPlay(new Position(10, 4));
        var selector = new SearchMoveSelector(7);
        Assert.Null(selector.SelectMove(game.Snapshot(), Stone.O,
            TimeSpan.FromSeconds(1), CancellationToken.None));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
        {
            _ = selector.Analyze(Position(x: [(10, 10)], o: []), Rules, Stone.O,
                TimeSpan.FromSeconds(1), cancellation.Token);
        });
    }

    [Fact]
    public void MidgameCompletesAtLeastOneSearchDepth()
    {
        var board = Position(
            x: [(2, 2), (2, 8), (2, 14), (8, 2), (8, 8), (8, 14)],
            o: [(5, 5), (5, 11), (5, 17), (11, 5), (11, 11), (11, 17)]);

        var result = new SearchMoveSelector(2026).Analyze(board, Rules, Stone.O,
            TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.Equal(SearchDecisionReason.Search, result.Reason);
        Assert.True(result.CompletedDepth >= 1);
        Assert.True(result.Nodes > 0);
        Assert.NotNull(result.Move);
        Assert.Equal(Stone.Empty, board.Get(result.Move.Value));
    }

    [Fact]
    public void ExpiredBudgetReturnsLegalFallback()
    {
        var board = Position(x: [(10, 10)], o: [(9, 9)]);

        var result = new SearchMoveSelector(2026).Analyze(board, Rules, Stone.X,
            TimeSpan.FromTicks(1), CancellationToken.None);

        Assert.Equal(SearchDecisionReason.Fallback, result.Reason);
        Assert.True(result.BudgetExpired);
        Assert.NotNull(result.Move);
        Assert.Equal(Stone.Empty, board.Get(result.Move.Value));
    }

    private static BoardSnapshot Position((int Row, int Column)[] x, (int Row, int Column)[] o)
    {
        var board = new Board(20);
        foreach (var (row, column) in x)
            Assert.True(board.TryPlace(new Position(row, column), Stone.X));
        foreach (var (row, column) in o)
            Assert.True(board.TryPlace(new Position(row, column), Stone.O));
        return board.Snapshot();
    }
}
