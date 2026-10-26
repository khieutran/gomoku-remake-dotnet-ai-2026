using Gomoku.Core;

namespace Gomoku.AI.Tests;

public sealed class ModelGuidedMoveSelectorTests
{
    private static readonly RuleSet Rules = new(20);

    [Fact]
    public void ExactPinnedVersionIsUsedOnlyWhenAvailable()
    {
        var registry = new FakeRegistry(new FixedModel(new float[400]));
        var selector = new ModelGuidedMoveSelector(registry, new SearchMoveSelector(2026));
        var game = AwaitingO();

        var selected = selector.Analyze(game, Stone.O, "search-v1+policy-approved",
            TimeSpan.MaxValue, CancellationToken.None);
        var missing = selector.Analyze(game, Stone.O, "search-v1+policy-old",
            TimeSpan.MaxValue, CancellationToken.None);

        Assert.Equal("search-v1+policy-old", registry.LastRequestedVersion);
        Assert.Equal("search-v1+policy-approved", selected.ActualAiVersion);
        Assert.Null(selected.FallbackReason);
        Assert.Equal(missing.Search.Reason == SearchDecisionReason.Fallback
            ? "legal-v1" : "search-v1", missing.ActualAiVersion);
        Assert.Equal("model_missing", missing.FallbackReason);
        AssertLegal(game, selected.Search.Move);
        AssertLegal(game, missing.Search.Move);
    }

    [Fact]
    public void OccupiedHighestLogitCannotBecomeMove()
    {
        var logits = new float[400];
        logits[10 * 20 + 10] = 1_000_000f; // occupied by X
        logits[10 * 20 + 11] = 100f;
        var selector = new ModelGuidedMoveSelector(
            new FakeRegistry(new FixedModel(logits)), new SearchMoveSelector(2026));
        var game = AwaitingO();

        var result = selector.Analyze(game, Stone.O, "search-v1+policy-approved",
            TimeSpan.MaxValue, CancellationToken.None);

        Assert.Equal("search-v1+policy-approved", result.ActualAiVersion);
        AssertLegal(game, result.Search.Move);
        Assert.NotEqual(new Position(10, 10), result.Search.Move);
    }

    [Fact]
    public void CoreImmediateWinOverridesPolicyPreference()
    {
        var game = Game.Start(Rules, Stone.O);
        var sequence = new[]
        {
            new Position(10, 7), new Position(0, 0),
            new Position(10, 8), new Position(0, 2),
            new Position(10, 9), new Position(0, 4),
            new Position(10, 10), new Position(10, 6)
        };
        foreach (var move in sequence)
            Assert.Equal(MoveResult.Played, game.TryPlay(move));
        var logits = new float[400];
        logits[15 * 20 + 15] = 1_000_000f;
        var selector = new ModelGuidedMoveSelector(
            new FakeRegistry(new FixedModel(logits)), new SearchMoveSelector(2026));

        var result = selector.Analyze(game.Snapshot(), Stone.O,
            "search-v1+policy-approved", TimeSpan.MaxValue, CancellationToken.None);

        Assert.Equal(new Position(10, 11), result.Search.Move);
        Assert.Equal(SearchDecisionReason.ImmediateWin, result.Search.Reason);
    }

    [Theory]
    [InlineData("model_load_error")]
    [InlineData("model_inference_error")]
    public void ModelFailureFallsBackToPureSearchWithReason(string reason)
    {
        var selector = new ModelGuidedMoveSelector(
            new FakeRegistry(new ThrowingModel(reason)), new SearchMoveSelector(2026));
        var game = AwaitingO();

        var result = selector.Analyze(game, Stone.O, "search-v1+policy-approved",
            TimeSpan.FromMilliseconds(100), CancellationToken.None);

        Assert.Equal(result.Search.Reason == SearchDecisionReason.Fallback
            ? "legal-v1" : "search-v1", result.ActualAiVersion);
        Assert.Equal(reason, result.FallbackReason);
        AssertLegal(game, result.Search.Move);
    }

    [Fact]
    public void InferenceThatExhaustsBudgetReturnsSearchFallback()
    {
        var selector = new ModelGuidedMoveSelector(
            new FakeRegistry(new DelayedModel()), new SearchMoveSelector(2026));
        var game = AwaitingO();

        var result = selector.Analyze(game, Stone.O, "search-v1+policy-approved",
            TimeSpan.FromTicks(1), CancellationToken.None);

        Assert.Equal("legal-v1", result.ActualAiVersion);
        Assert.Equal("model_budget_expired", result.FallbackReason);
        AssertLegal(game, result.Search.Move);
    }

    [Fact]
    public void NestedFallbackRecordsLegalEngineAfterModelFailure()
    {
        var selector = new ModelGuidedMoveSelector(
            new FakeRegistry(new ThrowingModel("model_load_error")),
            new SearchMoveSelector(2026));
        var game = AwaitingO();

        var result = selector.Analyze(game, Stone.O, "search-v1+policy-approved",
            TimeSpan.Zero, CancellationToken.None);

        Assert.Equal(SearchDecisionReason.Fallback, result.Search.Reason);
        Assert.Equal("legal-v1", result.ActualAiVersion);
        Assert.Equal("model_load_error", result.FallbackReason);
        AssertLegal(game, result.Search.Move);
    }

    private static GameSnapshot AwaitingO()
    {
        var game = Game.Start(Rules, Stone.X);
        Assert.Equal(MoveResult.Played, game.TryPlay(new Position(10, 10)));
        return game.Snapshot();
    }

    private static void AssertLegal(GameSnapshot game, Position? move)
    {
        Assert.NotNull(move);
        Assert.InRange(move.Value.Row, 0, 19);
        Assert.InRange(move.Value.Column, 0, 19);
        Assert.Equal(Stone.Empty, game.Board.Get(move.Value));
    }

    private sealed class FakeRegistry(IPolicyModel available) : IPolicyModelRegistry
    {
        public string? LastRequestedVersion { get; private set; }
        public IPolicyModel? Find(string aiVersion)
        {
            LastRequestedVersion = aiVersion;
            return aiVersion == "search-v1+policy-approved" ? available : null;
        }
    }

    private sealed class FixedModel(float[] logits) : IPolicyModel
    {
        public float[] Score(BoardSnapshot board, Stone side, CancellationToken token) => logits;
    }

    private sealed class ThrowingModel(string reason) : IPolicyModel
    {
        public float[] Score(BoardSnapshot board, Stone side, CancellationToken token) =>
            throw new PolicyModelException(reason);
    }

    private sealed class DelayedModel : IPolicyModel
    {
        public float[] Score(BoardSnapshot board, Stone side, CancellationToken token)
        {
            Thread.Sleep(1);
            return new float[400];
        }
    }
}
