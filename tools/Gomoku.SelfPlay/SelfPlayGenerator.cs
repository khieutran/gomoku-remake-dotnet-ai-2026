using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Gomoku.AI;
using Gomoku.Core;

namespace Gomoku.SelfPlay;

public enum SelfPlayMode { Bootstrap, Search }

public sealed record SelfPlaySettings(string Seed, int GameCount,
    SelfPlayMode Mode = SelfPlayMode.Bootstrap)
{
    public const int BoardSize = 20;
    public const string EngineVersion = "legal-v1";
    public const string SelectionMode = "seeded-legal-fallback-v1";
    public const string Split = "bootstrap";
    public const string SearchEngineVersion = "search-v1";
    public const string SearchSelectionMode = "fixed-depth-3-v1";
    public const string SearchSplit = "train-candidate";
}

public sealed record SelfPlayMove(int Step, int Row, int Column, string Stone,
    string DecisionReason = "Fallback", int CompletedDepth = 0,
    bool BudgetExpired = false);

public sealed record SelfPlayGame(int GameNumber, string Source, string Split,
    string RuleVersion, string EngineVersion, string SelectionMode,
    string OpeningStone, string Result, string ContentHash,
    IReadOnlyList<SelfPlayMove> Moves);

public sealed class SelfPlayGenerator
{
    public IReadOnlyList<SelfPlayGame> Generate(SelfPlaySettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.Seed);
        if (settings.GameCount < 1)
            throw new ArgumentOutOfRangeException(nameof(settings), "At least one game is required.");
        if (!Enum.IsDefined(settings.Mode))
            throw new ArgumentOutOfRangeException(nameof(settings), "Invalid self-play mode.");

        var rules = new RuleSet(SelfPlaySettings.BoardSize);
        var games = new List<SelfPlayGame>(settings.GameCount);
        for (var gameNumber = 1; gameNumber <= settings.GameCount; gameNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var opener = gameNumber % 2 == 1 ? Stone.X : Stone.O;
            var game = Game.Start(rules, opener);
            var moves = new List<SelfPlayMove>();
            while (game.Snapshot().Status == GameStatus.InProgress)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var snapshot = game.Snapshot();
                var side = snapshot.NextStone!.Value;
                var turnSeed = SeedForTurn(settings.Seed, gameNumber, snapshot.MoveIndex + 1);
                var decision = settings.Mode == SelfPlayMode.Search
                    ? SelectSearchTurn(snapshot, turnSeed, cancellationToken)
                    : new SearchMoveSelector(turnSeed).Analyze(snapshot, side,
                        TimeSpan.Zero, cancellationToken);
                if (settings.Mode == SelfPlayMode.Search &&
                    (decision.Reason is SearchDecisionReason.Fallback or
                        SearchDecisionReason.NoMove || decision.BudgetExpired))
                    throw new InvalidOperationException("Search self-play cannot use fallback.");
                if (decision.Move is null || game.TryPlay(decision.Move.Value) != MoveResult.Played)
                    throw new InvalidOperationException("The self-play selector returned an illegal move.");
                moves.Add(new SelfPlayMove(snapshot.MoveIndex + 1, decision.Move.Value.Row,
                    decision.Move.Value.Column, side.ToString(), decision.Reason.ToString(),
                    decision.CompletedDepth, decision.BudgetExpired));
            }

            var finished = game.Snapshot();
            if (finished.Status == GameStatus.InProgress)
                throw new InvalidOperationException("An unfinished game cannot enter the artifact.");
            games.Add(new SelfPlayGame(gameNumber, "self-play",
                settings.Mode == SelfPlayMode.Search ? SelfPlaySettings.SearchSplit : SelfPlaySettings.Split,
                rules.Version,
                settings.Mode == SelfPlayMode.Search ? SelfPlaySettings.SearchEngineVersion :
                    SelfPlaySettings.EngineVersion,
                settings.Mode == SelfPlayMode.Search ? SelfPlaySettings.SearchSelectionMode :
                    SelfPlaySettings.SelectionMode,
                opener.ToString(), finished.Status.ToString(), ContentHash(rules.Version,
                    opener, finished.Status, moves), moves));
        }
        return games;
    }

    public static SearchResult SelectSearchTurn(GameSnapshot snapshot, int seed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Status != GameStatus.InProgress || snapshot.NextStone is null)
            throw new ArgumentException("A playable game is required.", nameof(snapshot));
        // The search engine has a finite depth cap of three. MaxValue prevents
        // machine speed from changing which depth completes.
        return new SearchMoveSelector(seed).Analyze(snapshot, snapshot.NextStone.Value,
            TimeSpan.MaxValue, cancellationToken);
    }

    internal static int SeedForTurn(string seed, int gameNumber, int step)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{seed}:{gameNumber}:{step}"));
        return BinaryPrimitives.ReadInt32BigEndian(hash);
    }

    private static string ContentHash(string rules, Stone opener, GameStatus status,
        IReadOnlyList<SelfPlayMove> moves)
    {
        var canonical = new StringBuilder(rules).Append('|').Append(opener).Append('|').Append(status);
        foreach (var move in moves)
            canonical.Append('|').Append(move.Step).Append(':').Append(move.Row)
                .Append(':').Append(move.Column).Append(':').Append(move.Stone);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();
    }
}
