using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Gomoku.AI;
using Gomoku.Core;

namespace Gomoku.ModelEvaluation;

public sealed class SearchEvaluationPlayer(int seed) : IEvaluationPlayer
{
    private readonly SearchMoveSelector _search = new(seed);

    public MoveDecision Choose(GameSnapshot game, Stone side, TimeSpan budget,
        CancellationToken token)
    {
        var result = _search.Analyze(game, side, budget, token);
        return new(result.Move, result.BudgetExpired,
            result.BudgetExpired ? "search_budget_expired" : null);
    }
}

public sealed class ModelEvaluationPlayer(IPolicyModelRegistry registry, int seed,
    string candidateId) : IEvaluationPlayer
{
    private readonly ModelGuidedMoveSelector _selector =
        new(registry, new SearchMoveSelector(seed));

    public MoveDecision Choose(GameSnapshot game, Stone side, TimeSpan budget,
        CancellationToken token)
    {
        var result = _selector.Analyze(game, side, candidateId, budget, token);
        return new(result.Search.Move, result.Search.BudgetExpired,
            result.FallbackReason ?? (result.Search.BudgetExpired ?
                "search_budget_expired" : null));
    }
}

public sealed class MatchEvaluator(IEvaluationPlayer candidate, IEvaluationPlayer champion)
{
    public (MatchMetrics Matches, TacticalMetrics CandidateTactics,
        TacticalMetrics ChampionTactics, DecisionMetrics CandidateDecisions,
        DecisionMetrics ChampionDecisions) Evaluate(EvaluationConfig config,
            FixtureFile fixtures, CancellationToken token = default)
    {
        Validate(config, fixtures);
        var candidateDecisions = new DecisionAccumulator();
        var championDecisions = new DecisionAccumulator();
        var rules = new RuleSet(fixtures.BoardSize);
        var budget = TimeSpan.FromMilliseconds(config.MoveBudgetMs);
        var candidateTactics = EvaluateTactics(candidate, candidateDecisions,
            fixtures, rules, budget, token);
        var championTactics = EvaluateTactics(champion, championDecisions,
            fixtures, rules, budget, token);
        var asX = new int[3];
        var asO = new int[3];
        for (var opening = 0; opening < config.OpeningCount; opening++)
        {
            // Each opener is played twice, with the candidate assigned each color.
            var initialMoves = Opening(config.Seed, opening, rules.BoardSize);
            PlayOne(initialMoves, Stone.X, asX);
            PlayOne(initialMoves, Stone.O, asO);
        }
        var games = config.OpeningCount * 2;
        var score = (asX[0] + asO[0] + 0.5 * (asX[1] + asO[1])) / games;
        return (new MatchMetrics(games,
                new ColorResult(asX[0], asX[1], asX[2]),
                new ColorResult(asO[0], asO[1], asO[2]), score),
            candidateTactics, championTactics,
            candidateDecisions.Snapshot(), championDecisions.Snapshot());

        void PlayOne(Position[] initialMoves, Stone candidateStone, int[] scores)
        {
            var game = Game.Start(rules, Stone.X);
            foreach (var move in initialMoves)
                if (game.TryPlay(move) != MoveResult.Played)
                    throw new InvalidDataException("Seeded opener is invalid.");

            for (var ply = initialMoves.Length; ply < config.MaxPlies; ply++)
            {
                token.ThrowIfCancellationRequested();
                var snapshot = game.Snapshot();
                if (snapshot.Status != GameStatus.InProgress) break;
                var candidateTurn = snapshot.NextStone == candidateStone;
                var actor = candidateTurn ? candidate : champion;
                var accumulator = candidateTurn ? candidateDecisions : championDecisions;
                var decision = Decide(actor, accumulator, snapshot,
                    snapshot.NextStone!.Value, budget, token);
                if (decision.Move is null || game.TryPlay(decision.Move.Value) != MoveResult.Played)
                {
                    accumulator.IllegalMoves++;
                    scores[candidateTurn ? 2 : 0]++;
                    return;
                }
            }
            var status = game.Snapshot().Status;
            scores[status == GameStatus.InProgress || status == GameStatus.Draw ? 1 :
                (status == GameStatus.XWon) == (candidateStone == Stone.X) ? 0 : 2]++;
        }
    }

    private static TacticalMetrics EvaluateTactics(IEvaluationPlayer player,
        DecisionAccumulator accumulator, FixtureFile file, RuleSet rules,
        TimeSpan budget, CancellationToken token)
    {
        var failed = new List<string>();
        var total = 0;
        foreach (var fixture in file.Cases.Where(item => item.SideToMove is not null))
        {
            token.ThrowIfCancellationRequested();
            total++;
            var game = ReplayFixture(fixture, rules);
            var snapshot = game.Snapshot();
            if (snapshot.Status != GameStatus.InProgress ||
                snapshot.NextStone != fixture.SideToMove)
                throw new InvalidDataException($"Fixture {fixture.Id} is not a playable position.");
            var decision = Decide(player, accumulator, snapshot,
                fixture.SideToMove!.Value, budget, token);
            var legal = decision.Move is Position move &&
                game.TryPlay(move) == MoveResult.Played;
            var expected = fixture.ExpectedWinningMoves.Length > 0
                ? fixture.ExpectedWinningMoves
                : fixture.ExpectedBlockingMoves;
            var correct = legal && (expected.Length == 0 ||
                expected.Any(item => item.Length == 2 &&
                    item[0] == decision.Move!.Value.Row &&
                    item[1] == decision.Move.Value.Column));
            if (!legal) accumulator.IllegalMoves++;
            if (correct && fixture.CandidateChecks is not null)
            {
                foreach (var check in fixture.CandidateChecks)
                {
                    var checkedGame = ReplayFixture(fixture, rules);
                    if (check.Move.Length != 2 ||
                        checkedGame.TryPlay(new Position(check.Move[0], check.Move[1])) !=
                        MoveResult.Played ||
                        checkedGame.Snapshot().Status != check.ExpectedStatus)
                    {
                        correct = false;
                        break;
                    }
                }
            }
            if (!correct) failed.Add(fixture.Id);
        }
        return new(total - failed.Count, total, failed);
    }

    private static Game ReplayFixture(TacticalFixture fixture, RuleSet rules)
    {
        var game = Game.Start(rules, fixture.SideToMove!.Value);
        var count = Math.Max(fixture.X.Length, fixture.O.Length);
        for (var index = 0; index < count; index++)
        {
            // The synthetic fixtures have equal counts; starting with the side to move
            // restores the declared next side after an even number of moves.
            foreach (var stone in new[] { fixture.SideToMove.Value,
                         fixture.SideToMove == Stone.X ? Stone.O : Stone.X })
            {
                var coordinates = stone == Stone.X ? fixture.X : fixture.O;
                if (index >= coordinates.Length) continue;
                var point = coordinates[index];
                if (point.Length != 2 ||
                    game.TryPlay(new Position(point[0], point[1])) != MoveResult.Played)
                    throw new InvalidDataException($"Invalid tactical fixture: {fixture.Id}");
            }
        }
        return game;
    }

    private static MoveDecision Decide(IEvaluationPlayer player,
        DecisionAccumulator accumulator, GameSnapshot snapshot, Stone side,
        TimeSpan budget, CancellationToken token)
    {
        var started = Stopwatch.GetTimestamp();
        MoveDecision decision;
        try { decision = player.Choose(snapshot, side, budget, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            decision = new(null, false, "selector_exception");
        }
        var elapsed = Stopwatch.GetElapsedTime(started);
        accumulator.Latencies.Add(elapsed.TotalMilliseconds);
        if (decision.BudgetExpired || elapsed > budget ||
            decision.FallbackReason?.Contains("budget", StringComparison.Ordinal) == true)
            accumulator.Timeouts++;
        if (decision.FallbackReason is not null) accumulator.Fallbacks++;
        if (decision.FallbackReason is not null &&
            !decision.FallbackReason.Contains("budget", StringComparison.Ordinal))
            accumulator.ModelErrors++;
        return decision;
    }

    private static Position[] Opening(string seed, int index, int boardSize)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{seed}:{index}"));
        var row = boardSize / 2 - 2 + hash[0] % 5;
        var col = boardSize / 2 - 2 + hash[1] % 5;
        var nextCol = col + (col == boardSize - 1 ? -1 : 1);
        return [new(row, col), new(row, nextCol)];
    }

    public static void Validate(EvaluationConfig config, FixtureFile fixtures)
    {
        if (string.IsNullOrWhiteSpace(config.CandidateId) ||
            config.CandidateId == config.ChampionId ||
            string.IsNullOrWhiteSpace(config.ChampionId) ||
            (config.ChampionId == "search-v1" && config.ChampionModelPath is not null) ||
            (config.ChampionId != "search-v1" &&
                string.IsNullOrWhiteSpace(config.ChampionModelPath)) ||
            string.IsNullOrWhiteSpace(config.Seed) ||
            string.IsNullOrWhiteSpace(config.DatasetHash) ||
            config.OpeningCount < 1 || config.MoveBudgetMs < 1 ||
            config.MaxPlies < 4 || config.MaxPlies > 400 ||
            config.MinimumCandidateScore is < 0.55 or > 1 ||
            config.MinimumTacticalPassRate != 1 ||
            config.MaximumFallbackRate is < 0 or > 0.05 ||
            config.MaximumTimeouts != 0 ||
            fixtures.BoardSize != 20 ||
            fixtures.RuleVersion != RuleSet.CurrentVersion ||
            fixtures.Cases.Count == 0)
            throw new InvalidDataException("Invalid evaluation config or fixture contract.");
    }

    private sealed class DecisionAccumulator
    {
        public List<double> Latencies { get; } = [];
        public int Timeouts;
        public int Fallbacks;
        public int ModelErrors;
        public int IllegalMoves;

        public DecisionMetrics Snapshot()
        {
            var values = Latencies.Order().ToArray();
            return new(values.Length, Percentile(values, .5), Percentile(values, .95),
                Timeouts, Fallbacks, ModelErrors, IllegalMoves);
        }

        private static double Percentile(double[] sorted, double percentile)
        {
            if (sorted.Length == 0) return 0;
            var rank = (int)Math.Ceiling(percentile * sorted.Length) - 1;
            return Math.Round(sorted[Math.Max(0, rank)], 3);
        }
    }
}
