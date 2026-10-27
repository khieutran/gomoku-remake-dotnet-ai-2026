using Gomoku.Core;

namespace Gomoku.AI;

internal sealed class AlphaBetaSearch(SearchBoard board, RuleSet rules, Stone side,
    int seed, SearchClock clock, CancellationToken cancellationToken,
    int[]? policyRanks = null)
{
    private const int MaxDepth = 3;
    private const int MateScore = 1_000_000;
    private const int Infinity = 2_000_000;

    public SearchResult Run(Position fallback)
    {
        var best = fallback;
        var completedDepth = 0;
        var expired = false;
        for (var depth = 1; depth <= MaxDepth; depth++)
        {
            try
            {
                var (move, score) = SearchRoot(depth);
                if (move is not null) best = move.Value;
                completedDepth = depth;
                if (score >= MateScore) break;
            }
            catch (SearchBudgetExpiredException)
            {
                expired = true;
                break;
            }
        }

        return new SearchResult(best,
            completedDepth > 0 ? SearchDecisionReason.Search : SearchDecisionReason.Fallback,
            completedDepth, clock.Nodes, expired);
    }

    private (Position? Move, int Score) SearchRoot(int depth)
    {
        var candidates = OrderedCandidates(side, 16, true);
        Position? bestMove = null;
        var bestScore = -Infinity;
        var alpha = -Infinity;
        foreach (var move in candidates)
        {
            clock.Check();
            board.Play(move, side);
            int score;
            try { score = -Negamax(Other(side), depth - 1, move, side, -Infinity, -alpha); }
            finally { board.Undo(move); }
            if (score > bestScore)
            {
                bestScore = score;
                bestMove = move;
            }
            alpha = Math.Max(alpha, score);
        }
        return (bestMove, bestScore);
    }

    private int Negamax(Stone turn, int depth, Position lastMove, Stone lastStone,
        int alpha, int beta)
    {
        clock.Visit();
        var status = RuleEvaluator.Evaluate(board, lastMove, lastStone, rules);
        if (status is GameStatus.XWon or GameStatus.OWon) return -MateScore - depth;
        if (status == GameStatus.Draw) return 0;
        if (depth == 0)
            return PatternEvaluator.Score(board, turn) - PatternEvaluator.Score(board, Other(turn));

        var best = -Infinity;
        foreach (var move in OrderedCandidates(turn, 8))
        {
            clock.Check();
            board.Play(move, turn);
            int score;
            try { score = -Negamax(Other(turn), depth - 1, move, turn, -beta, -alpha); }
            finally { board.Undo(move); }
            best = Math.Max(best, score);
            alpha = Math.Max(alpha, score);
            if (alpha >= beta) break;
        }
        return best == -Infinity ? 0 : best;
    }

    private IReadOnlyList<Position> OrderedCandidates(Stone turn, int limit,
        bool usePolicy = false)
    {
        var opponent = Other(turn);
        var candidates = LegalMoveCandidates.NearOccupied(board, cancellationToken);
        var ranked = new List<(Position Move, int Score)>(candidates.Count);
        foreach (var move in candidates)
        {
            clock.Check();
            board.Play(move, turn);
            try
            {
                var status = RuleEvaluator.Evaluate(board, move, turn, rules);
                var score = Won(status, turn) ? MateScore :
                    PatternEvaluator.Score(board, turn) - PatternEvaluator.Score(board, opponent);
                // Policy only adjusts root ordering. It cannot replace Core tactical checks.
                if (usePolicy && policyRanks is not null)
                    score += policyRanks[move.Row * board.Size + move.Column] * 20;
                ranked.Add((move, score));
            }
            finally { board.Undo(move); }
        }
        return ranked.OrderByDescending(item => item.Score)
            .ThenBy(item => Math.Abs(item.Move.Row - board.Size / 2) +
                Math.Abs(item.Move.Column - board.Size / 2))
            .ThenBy(item => TieBreak(item.Move))
            .Take(limit).Select(item => item.Move).ToArray();
    }

    private uint TieBreak(Position move) => unchecked((uint)seed * 2_654_435_761u +
        (uint)move.Row * 2_246_822_519u + (uint)move.Column * 3_266_489_917u);

    private static bool Won(GameStatus status, Stone stone) =>
        stone == Stone.X ? status == GameStatus.XWon : status == GameStatus.OWon;

    private static Stone Other(Stone stone) => stone == Stone.X ? Stone.O : Stone.X;
}
