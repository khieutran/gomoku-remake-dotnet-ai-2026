using System.Text.Json;
using Gomoku.Core;

namespace Gomoku.Core.Tests;

public sealed class LegacyParityTests
{
    public static IEnumerable<object[]> GameCaseIds() => LoadCorpus().Games.Select(game => new object[] { game.Id });

    public static IEnumerable<object[]> MatchCaseIds() => LoadCorpus().Matches.Select(match => new object[] { match.Id });

    [Theory]
    [MemberData(nameof(GameCaseIds))]
    public void ReplayingRecordedMovesReconstructsGame(string id)
    {
        var corpus = LoadCorpus();
        var fixture = corpus.Games.Single(game => game.Id == id);
        var game = Replay(fixture, corpus.RuleVersion);
        var snapshot = game.Snapshot();

        Assert.Equal(ParseStatus(fixture.ExpectedStatus), snapshot.Status);
        Assert.Equal(ParseOptionalStone(fixture.ExpectedNextStone), snapshot.NextStone);
        Assert.Equal(fixture.ExpectedMoveIndex, snapshot.MoveIndex);
        Assert.Equal(fixture.BoardSize, snapshot.Rules.BoardSize);
        Assert.Equal(corpus.RuleVersion, snapshot.Rules.Version);
        AssertBoardMatchesRecordedMoves(snapshot, fixture);
    }

    [Theory]
    [MemberData(nameof(GameCaseIds))]
    public void ReplayingSameMovesProducesIdenticalSnapshot(string id)
    {
        var corpus = LoadCorpus();
        var fixture = corpus.Games.Single(game => game.Id == id);
        var first = Replay(fixture, corpus.RuleVersion).Snapshot();
        var second = Replay(fixture, corpus.RuleVersion).Snapshot();

        Assert.Equal(first.Status, second.Status);
        Assert.Equal(first.NextStone, second.NextStone);
        Assert.Equal(first.MoveIndex, second.MoveIndex);
        Assert.Equal(first.Rules.Version, second.Rules.Version);
        Assert.Equal<Move>(first.Moves, second.Moves);
        for (var row = 0; row < fixture.BoardSize; row++)
        {
            for (var column = 0; column < fixture.BoardSize; column++)
            {
                var position = new Position(row, column);
                Assert.Equal(first.Board.Get(position), second.Board.Get(position));
            }
        }
    }

    [Theory]
    [MemberData(nameof(MatchCaseIds))]
    public void ReplayingCompletedGamesReconstructsMatchScoreAndOpeningOrder(string id)
    {
        var corpus = LoadCorpus();
        var fixture = corpus.Matches.Single(match => match.Id == id);
        var match = Match.Start(new RuleSet(fixture.BoardSize));
        Assert.Equal(fixture.GameIds.Count, fixture.ExpectedScores.Count);

        for (var gameIndex = 0; gameIndex < fixture.GameIds.Count; gameIndex++)
        {
            var gameFixture = corpus.Games.Single(game => game.Id == fixture.GameIds[gameIndex]);
            Assert.Equal(fixture.BoardSize, gameFixture.BoardSize);
            Assert.Equal(ParseStone(gameFixture.OpeningStone), match.Snapshot().OpeningStone);
            foreach (var move in gameFixture.Moves)
            {
                Assert.Equal(MoveResult.Played, match.TryPlay(new Position(move.Row, move.Column)));
                Assert.Equal(move.Step, match.Snapshot().CurrentGame.MoveIndex);
            }

            var completed = match.Snapshot();
            var expectedScore = fixture.ExpectedScores[gameIndex];
            Assert.Equal(expectedScore.X, completed.XScore);
            Assert.Equal(expectedScore.O, completed.OScore);
            Assert.Equal(ParseStatus(gameFixture.ExpectedStatus), completed.CurrentGame.Status);
            Assert.Equal(corpus.RuleVersion, completed.CurrentGame.Rules.Version);
            AssertBoardMatchesRecordedMoves(completed.CurrentGame, gameFixture);

            Assert.True(match.StartNextGame());
            var next = match.Snapshot();
            Assert.Equal(gameIndex + 1, next.CompletedGames.Count);
            Assert.Equal(completed.CurrentGame.MoveIndex, next.CompletedGames[gameIndex].MoveIndex);
            Assert.Equal(completed.CurrentGame.Status, next.CompletedGames[gameIndex].Status);
            Assert.Equal<Move>(completed.CurrentGame.Moves, next.CompletedGames[gameIndex].Moves);
            AssertBoardMatchesRecordedMoves(next.CompletedGames[gameIndex], gameFixture);
            Assert.Equal(ParseStatus(gameFixture.ExpectedStatus), completed.CurrentGame.Status);
            AssertBoardMatchesRecordedMoves(completed.CurrentGame, gameFixture);
            Assert.Equal(expectedScore.X, completed.XScore);
            Assert.Equal(expectedScore.O, completed.OScore);
        }

        var after = match.Snapshot();
        Assert.Equal(ParseStone(fixture.ExpectedNextOpening), after.OpeningStone);
        Assert.Equal(ParseStone(fixture.ExpectedNextOpening), after.CurrentGame.NextStone);
        Assert.Equal(fixture.GameIds.Count, after.CompletedGames.Count);
    }

    [Fact]
    public void OldSnapshotsAndMatchesStayUnchangedAfterMorePlayOrNewMatch()
    {
        var corpus = LoadCorpus();
        var fixture = corpus.Games.Single(game => game.Id == "WIN-H");
        var game = Game.Start(new RuleSet(fixture.BoardSize), Stone.X);
        var empty = game.Snapshot();
        Assert.Equal(MoveResult.Played, game.TryPlay(new Position(fixture.Moves[0].Row, fixture.Moves[0].Column)));
        Assert.Equal(0, empty.MoveIndex);
        Assert.Equal(Stone.Empty, empty.Board.Get(new Position(10, 7)));

        var oldMatch = Match.Start(new RuleSet(fixture.BoardSize));
        foreach (var move in fixture.Moves)
        {
            Assert.Equal(MoveResult.Played, oldMatch.TryPlay(new Position(move.Row, move.Column)));
        }

        var oldSnapshot = oldMatch.Snapshot();
        var newMatch = Match.Start(new RuleSet(fixture.BoardSize));
        Assert.Equal(3, oldMatch.Snapshot().XScore);
        Assert.Equal(3, oldSnapshot.XScore);
        Assert.Equal(GameStatus.XWon, oldSnapshot.CurrentGame.Status);
        Assert.Equal(0, newMatch.Snapshot().XScore);
        Assert.Equal(0, newMatch.Snapshot().CurrentGame.MoveIndex);
        Assert.Equal(Stone.X, newMatch.Snapshot().OpeningStone);
    }

    private static Game Replay(GameFixture fixture, string expectedRuleVersion)
    {
        var game = Game.Start(new RuleSet(fixture.BoardSize), ParseStone(fixture.OpeningStone));
        Assert.Equal(expectedRuleVersion, game.Snapshot().Rules.Version);
        foreach (var move in fixture.Moves)
        {
            Assert.Equal(MoveResult.Played, game.TryPlay(new Position(move.Row, move.Column)));
            var snapshot = game.Snapshot();
            Assert.Equal(move.Step, snapshot.MoveIndex);
            Assert.Equal(new Move(move.Step, new Position(move.Row, move.Column), ParseStone(move.Stone)),
                snapshot.Moves[^1]);
        }

        return game;
    }

    private static void AssertBoardMatchesRecordedMoves(GameSnapshot snapshot, GameFixture fixture)
    {
        var expected = fixture.Moves.ToDictionary(move => new Position(move.Row, move.Column),
            move => ParseStone(move.Stone));
        Assert.Equal(fixture.Moves.Count, expected.Count);
        for (var row = 0; row < fixture.BoardSize; row++)
        {
            for (var column = 0; column < fixture.BoardSize; column++)
            {
                var position = new Position(row, column);
                Assert.Equal(expected.GetValueOrDefault(position, Stone.Empty), snapshot.Board.Get(position));
            }
        }
    }

    private static Stone ParseStone(string value) => Enum.Parse<Stone>(value);

    private static Stone? ParseOptionalStone(string? value) => value is null ? null : ParseStone(value);

    private static GameStatus ParseStatus(string value) => Enum.Parse<GameStatus>(value);

    private static ReplayCorpus LoadCorpus()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "legacy-cases.json");
        var corpus = JsonSerializer.Deserialize<ReplayCorpus>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(corpus);
        Assert.Equal(1, corpus.SchemaVersion);
        return corpus;
    }

    private sealed class ReplayCorpus
    {
        public required int SchemaVersion { get; init; }
        public required string RuleVersion { get; init; }
        public required List<GameFixture> Games { get; init; }
        public required List<MatchFixture> Matches { get; init; }
    }

    private sealed class GameFixture
    {
        public required string Id { get; init; }
        public required string Source { get; init; }
        public required int BoardSize { get; init; }
        public required string OpeningStone { get; init; }
        public required List<RecordedMove> Moves { get; init; }
        public required int ExpectedMoveIndex { get; init; }
        public required string ExpectedStatus { get; init; }
        public string? ExpectedNextStone { get; init; }
    }

    private sealed class RecordedMove
    {
        public required int Step { get; init; }
        public required int Row { get; init; }
        public required int Column { get; init; }
        public required string Stone { get; init; }
    }

    private sealed class MatchFixture
    {
        public required string Id { get; init; }
        public required int BoardSize { get; init; }
        public required List<string> GameIds { get; init; }
        public required List<ExpectedScore> ExpectedScores { get; init; }
        public required string ExpectedNextOpening { get; init; }
    }

    private sealed class ExpectedScore
    {
        public required int X { get; init; }
        public required int O { get; init; }
    }
}
