using Gomoku.Core;

namespace Gomoku.AI;

internal sealed record TacticalChoice(Position Move, SearchDecisionReason Reason);

internal static class ThreatBalanceEvaluator
{
    private const int MinimumThreatScore = 80;
    private const int MaxAttackCandidates = 8;

    public static TacticalChoice? Choose(SearchBoard board, IReadOnlyList<Position> candidates,
        Stone side, SearchClock clock)
    {
        var opponent = side == Stone.X ? Stone.O : Stone.X;
        var scored = new List<(Position Move, int Attack, int Defense)>(candidates.Count);
        foreach (var move in candidates)
        {
            clock.Check();
            var attack = ScoreMove(board, move, side);
            var defense = ScoreMove(board, move, opponent);
            scored.Add((move, attack, defense));
        }

        var bestDefense = scored.OrderByDescending(item => item.Defense).FirstOrDefault();
        Position? bestAttack = null;
        var bestMargin = 0;
        var bestAttackScore = 0;
        foreach (var candidate in scored.Where(item => item.Attack >= MinimumThreatScore)
                     .OrderByDescending(item => item.Attack)
                     .ThenBy(item => item.Move.Row)
                     .ThenBy(item => item.Move.Column)
                     .Take(MaxAttackCandidates))
        {
            clock.Check();
            board.Play(candidate.Move, side);
            int responseRisk;
            try
            {
                responseRisk = StrongestResponse(board, candidates, opponent, clock);
            }
            finally { board.Undo(candidate.Move); }

            var margin = candidate.Attack - responseRisk;
            if (margin <= 0 || margin < bestMargin ||
                (margin == bestMargin && candidate.Attack <= bestAttackScore)) continue;
            bestAttack = candidate.Move;
            bestMargin = margin;
            bestAttackScore = candidate.Attack;
        }

        if (bestAttack is { } attackMove)
            return new TacticalChoice(attackMove, SearchDecisionReason.ThreatAttack);
        return bestDefense.Defense >= MinimumThreatScore
            ? new TacticalChoice(bestDefense.Move, SearchDecisionReason.ThreatBlock)
            : null;
    }

    private static int StrongestResponse(SearchBoard board, IReadOnlyList<Position> candidates,
        Stone opponent, SearchClock clock)
    {
        var strongest = 0;
        foreach (var move in candidates)
        {
            clock.Check();
            if (board.Get(move) != Stone.Empty) continue;
            strongest = Math.Max(strongest, ScoreMove(board, move, opponent));
        }
        return strongest;
    }

    private static int ScoreMove(SearchBoard board, Position move, Stone side)
    {
        board.Play(move, side);
        try { return PatternEvaluator.ScoreAt(board, move, side); }
        finally { board.Undo(move); }
    }
}
