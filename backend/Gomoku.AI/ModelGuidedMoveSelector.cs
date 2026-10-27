using System.Diagnostics;
using Gomoku.Core;

namespace Gomoku.AI;

public sealed record VersionedSearchResult(SearchResult Search, string ActualAiVersion,
    string? FallbackReason);

/// <summary>Uses a pinned policy to order search candidates. Core rules still decide tactics.</summary>
public sealed class ModelGuidedMoveSelector(IPolicyModelRegistry models, SearchMoveSelector search)
{
    public VersionedSearchResult Analyze(GameSnapshot game, Stone side, string aiVersion,
        TimeSpan budget, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentException.ThrowIfNullOrWhiteSpace(aiVersion);
        cancellationToken.ThrowIfCancellationRequested();
        if (game.Status != GameStatus.InProgress)
            return new(new SearchResult(null, SearchDecisionReason.NoMove, 0, 0, false),
                aiVersion, null);
        if (game.NextStone != side)
            throw new ArgumentException("The requested side is not next to play.", nameof(side));
        if (budget < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(budget));

        var started = Stopwatch.GetTimestamp();
        try
        {
            var model = models.Find(aiVersion)
                ?? throw new PolicyModelException("model_missing");
            var logits = model.Score(game.Board, side, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var ranked = MaskAndRank(game.Board, logits);
            var remaining = budget - Stopwatch.GetElapsedTime(started);
            if (remaining <= TimeSpan.Zero)
                throw new PolicyModelException("model_budget_expired");
            var result = search.Analyze(game.Board, game.Rules, side, remaining,
                cancellationToken, ranked);
            return result.Reason == SearchDecisionReason.Fallback
                ? new(result, "legal-v1", result.BudgetExpired
                    ? "ai_budget_expired" : "ai_selector_fallback")
                : new(result, aiVersion, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Fallback("model_budget_expired");
        }
        catch (PolicyModelException exception)
        {
            return Fallback(exception.Reason);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Fallback("model_inference_error");
        }

        VersionedSearchResult Fallback(string reason)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = budget - Stopwatch.GetElapsedTime(started);
            var result = search.Analyze(game, side, remaining > TimeSpan.Zero
                ? remaining : TimeSpan.Zero, cancellationToken);
            return new(result, result.Reason == SearchDecisionReason.Fallback
                ? "legal-v1" : "search-v1", reason);
        }
    }

    private static int[] MaskAndRank(BoardSnapshot board, float[] logits)
    {
        if (board.Size != 20 || logits.Length != 400)
            throw new PolicyModelException("model_invalid_output");
        var legal = new List<(int Index, float Logit)>(400);
        for (var row = 0; row < 20; row++)
        for (var column = 0; column < 20; column++)
        {
            var index = row * 20 + column;
            if (board.Get(new Position(row, column)) != Stone.Empty) continue;
            if (!float.IsFinite(logits[index]))
                throw new PolicyModelException("model_invalid_output");
            legal.Add((index, logits[index]));
        }
        var ranks = new int[400];
        var ordered = legal.OrderByDescending(item => item.Logit)
            .ThenBy(item => item.Index).ToArray();
        for (var rank = 0; rank < ordered.Length; rank++)
            ranks[ordered[rank].Index] = ordered.Length - rank;
        return ranks;
    }
}
