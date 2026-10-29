using Gomoku.Core;

namespace Gomoku.AI;

public sealed class SearchMoveSelector(int seed) : IAiMoveAnalyzer
{
    public Position? SelectMove(GameSnapshot game, Stone side, TimeSpan budget,
        CancellationToken cancellationToken) => Analyze(game, side, budget, cancellationToken).Move;

    public SearchResult Analyze(GameSnapshot game, Stone side, TimeSpan budget,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        cancellationToken.ThrowIfCancellationRequested();
        if (game.Status != GameStatus.InProgress)
            return new SearchResult(null, SearchDecisionReason.NoMove, 0, 0, false);
        if (game.NextStone != side)
            throw new ArgumentException("The requested side is not next to play.", nameof(side));
        return Analyze(game.Board, game.Rules, side, budget, cancellationToken);
    }

    public SearchResult Analyze(BoardSnapshot snapshot, RuleSet rules, Stone side,
        TimeSpan budget, CancellationToken cancellationToken)
        => Analyze(snapshot, rules, side, budget, cancellationToken, null);

    public SearchResult Analyze(BoardSnapshot snapshot, RuleSet rules, Stone side,
        TimeSpan budget, CancellationToken cancellationToken, int[]? policyRanks)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(rules);
        cancellationToken.ThrowIfCancellationRequested();
        if (snapshot.Size != rules.BoardSize || snapshot.Columns != rules.BoardColumns)
            throw new ArgumentException("Board size does not match rules.", nameof(snapshot));
        if (side is not (Stone.X or Stone.O))
            throw new ArgumentOutOfRangeException(nameof(side));
        if (budget < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(budget));
        if (policyRanks is not null && policyRanks.Length != snapshot.Size * snapshot.Columns)
            throw new ArgumentException("Policy rank count does not match board.", nameof(policyRanks));

        var board = new SearchBoard(snapshot);
        var clock = new SearchClock(budget, cancellationToken);
        var fallback = LegalFallback.Choose(board, seed, cancellationToken);
        if (fallback is null)
            return new SearchResult(null, SearchDecisionReason.NoMove, 0, 0, false);
        if (budget == TimeSpan.Zero)
            return new SearchResult(fallback, SearchDecisionReason.Fallback, 0, 0, true);
        if (board.OccupiedCount == 0)
            return new SearchResult(fallback, SearchDecisionReason.Search, 0, 0, false);

        try
        {
            var candidates = LegalMoveCandidates.NearOccupied(board, cancellationToken);
            foreach (var move in candidates)
            {
                clock.Check();
                board.Play(move, side);
                try
                {
                    if (Won(RuleEvaluator.Evaluate(board, move, side, rules), side))
                        return new SearchResult(move, SearchDecisionReason.ImmediateWin, 0,
                            clock.Nodes, false);
                }
                finally { board.Undo(move); }
            }

            var opponent = Other(side);
            foreach (var move in candidates)
            {
                clock.Check();
                board.Play(move, opponent);
                try
                {
                    if (Won(RuleEvaluator.Evaluate(board, move, opponent, rules), opponent))
                        return new SearchResult(move, SearchDecisionReason.ImmediateBlock, 0,
                            clock.Nodes, false);
                }
                finally { board.Undo(move); }
            }

            var tactical = ThreatBalanceEvaluator.Choose(board, candidates, side, clock);
            if (tactical is not null)
                return new SearchResult(tactical.Move, tactical.Reason, 0,
                    clock.Nodes, false);

            return new AlphaBetaSearch(board, rules, side, seed, clock, cancellationToken,
                    policyRanks)
                .Run(fallback.Value);
        }
        catch (SearchBudgetExpiredException)
        {
            return new SearchResult(fallback, SearchDecisionReason.Fallback, 0,
                clock.Nodes, true);
        }
    }

    private static bool Won(GameStatus status, Stone side) =>
        side == Stone.X ? status == GameStatus.XWon : status == GameStatus.OWon;

    private static Stone Other(Stone side) => side == Stone.X ? Stone.O : Stone.X;
}
