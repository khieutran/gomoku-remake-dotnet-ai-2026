using Gomoku.Application.Datasets;
using Gomoku.Application.Identity;
using Gomoku.Application.Matches;
using Gomoku.Core;
using Xunit;

namespace Gomoku.Application.Tests.Datasets;

public sealed class GuestDatasetBuilderTests
{
    private static readonly DateTimeOffset Cutoff = new(2026, 10, 26, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ExportsOnlyFinishedReplayableGuestGameWithPinnedProvenance()
    {
        var builder = NewBuilder();
        var valid = CompletedMatch(Guid.Parse("00000000-0000-0000-0000-000000000001"), OwnerScope.Guest);
        var account = CompletedMatch(Guid.Parse("00000000-0000-0000-0000-000000000002"), OwnerScope.Account);
        var missingVersion = CompletedMatch(Guid.Parse("00000000-0000-0000-0000-000000000003"),
            OwnerScope.Guest, aiVersion: null);
        var invalid = CompletedMatch(Guid.Parse("00000000-0000-0000-0000-000000000004"),
            OwnerScope.Guest, badFinalMove: true);

        Assert.Empty(builder.Process(account));
        Assert.Empty(builder.Process(missingVersion));
        Assert.Empty(builder.Process(invalid));
        var row = Assert.Single(builder.Process(valid));

        Assert.Equal("guest", row.Source);
        Assert.Equal("gomoku-exact-five-v1", row.RuleVersion);
        Assert.Equal("search-v1", row.AiVersion);
        Assert.Equal("XWon", row.Result);
        Assert.Equal(9, row.Moves.Count);
        Assert.All(row.Moves.Where(move => move.Actor == "AI"),
            move => Assert.Equal("search-v1", move.ActualAiVersion));
        Assert.Equal(1, builder.Rejections["account_owner"]);
        Assert.Equal(1, builder.Rejections["missing_ai_version"]);
        Assert.Equal(1, builder.Rejections["invalid_replay"]);
    }

    [Fact]
    public void DeduplicatesByPlaySequenceAcrossMatches()
    {
        var builder = NewBuilder();
        var first = CompletedMatch(Guid.Parse("00000000-0000-0000-0000-000000000011"), OwnerScope.Guest);
        var second = CompletedMatch(Guid.Parse("00000000-0000-0000-0000-000000000012"), OwnerScope.Guest);

        Assert.Single(builder.Process(first));
        Assert.Empty(builder.Process(second));
        Assert.Equal(1, builder.Rejections["duplicate_game"]);
    }

    [Fact]
    public void RejectsAiMoveWithUnexplainedVersionChange()
    {
        var source = CompletedMatch(Guid.NewGuid(), OwnerScope.Guest);
        var game = source.Match.Games[0];
        var moves = game.Moves.Select(move => move.Actor == PlayerActor.AI
            ? move with { ActualAiVersion = "legal-v1", AiFallbackReason = null }
            : move).ToArray();
        source = source with
        {
            Match = source.Match with
            {
                Games = [game with { Moves = moves }]
            }
        };

        var builder = NewBuilder();

        Assert.Empty(builder.Process(source));
        Assert.Equal(1, builder.Rejections["inconsistent_ai_provenance"]);
    }

    [Fact]
    public void SameSeedAndCutoffProduceTheSameRow()
    {
        var source = CompletedMatch(Guid.Parse("00000000-0000-0000-0000-000000000021"), OwnerScope.Guest);

        var first = Assert.Single(NewBuilder().Process(source));
        var second = Assert.Single(NewBuilder().Process(source));

        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(first),
            System.Text.Json.JsonSerializer.Serialize(second));
        Assert.DoesNotContain(source.Match.Owner.Id.ToString("N"),
            System.Text.Json.JsonSerializer.Serialize(first), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(source.Match.Id.ToString("N"),
            System.Text.Json.JsonSerializer.Serialize(first), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GamesOfOneMatchStayInOneSplitAcrossAlternatingOpeners()
    {
        var source = CompletedMatch(Guid.NewGuid(), OwnerScope.Guest);
        var secondGameId = Guid.NewGuid();
        var secondMoves = new List<MoveRecord>();
        for (var column = 0; column < 4; column++)
        {
            secondMoves.Add(new MoveRecord(secondMoves.Count + 1, 0, column,
                Stone.O, PlayerActor.AI, "search-v1"));
            secondMoves.Add(new MoveRecord(secondMoves.Count + 1, 10, column,
                Stone.X, PlayerActor.Human));
        }
        secondMoves.Add(new MoveRecord(9, 0, 4, Stone.O, PlayerActor.AI, "search-v1"));
        var secondGame = new GameRecord(secondGameId, 2, Stone.O, GameStatus.OWon,
            GameRecordState.Finished, secondMoves, "search-v1");
        source = source with
        {
            Match = source.Match with
            {
                Games = [source.Match.Games[0], secondGame],
                CurrentGameId = secondGameId,
                Revision = 19,
                OScore = 3
            },
            FinishedAtByGame = new Dictionary<Guid, DateTimeOffset?>
            {
                [source.Match.CurrentGameId] = Cutoff.AddHours(-2),
                [secondGameId] = Cutoff.AddHours(-1)
            }
        };

        var rows = new GuestDatasetBuilder(new DatasetExportSettings("stable-seed",
            Cutoff.AddDays(1), Cutoff)).Process(source);

        Assert.Equal(2, rows.Count);
        Assert.Equal(rows[0].Split, rows[1].Split);
        Assert.Equal(new[] { "X", "O" }, rows.Select(row => row.OpeningStone));
    }

    [Fact]
    public void GamesAfterFrozenTestCutoffNeverEnterTraining()
    {
        var settings = new DatasetExportSettings("stable-seed", Cutoff.AddDays(1), Cutoff);
        var testMatchId = Enumerable.Range(1, 1000)
            .Select(value => new Guid(value, 0, 0, new byte[8]))
            .First(id => GuestDatasetBuilder.SplitFor(id, settings.Seed) == DatasetSplit.Test);
        var source = CompletedMatch(testMatchId, OwnerScope.Guest,
            finishedAt: Cutoff.AddHours(1));

        var builder = new GuestDatasetBuilder(settings);

        Assert.Empty(builder.Process(source));
        Assert.Equal(1, builder.Rejections["frozen_test_cutoff"]);
    }

    [Fact]
    public void NewMatchAfterTestCutoffCanEnterTrainOrValidation()
    {
        var settings = new DatasetExportSettings("stable-seed", Cutoff.AddDays(1), Cutoff);
        var matchId = Enumerable.Range(1, 1000)
            .Select(value => new Guid(value, 0, 0, new byte[8]))
            .First(id => GuestDatasetBuilder.SplitFor(id, settings.Seed) == DatasetSplit.Test);
        var source = CompletedMatch(matchId, OwnerScope.Guest,
            finishedAt: Cutoff.AddHours(2)) with { CreatedAt = Cutoff.AddHours(1) };

        var row = Assert.Single(new GuestDatasetBuilder(settings).Process(source));

        Assert.Contains(row.Split, new[] { "train", "validation" });
    }

    private static GuestDatasetBuilder NewBuilder() => new(
        new DatasetExportSettings("stable-seed", Cutoff.AddDays(1), Cutoff));

    private static GuestMatchSource CompletedMatch(Guid matchId, OwnerScope scope,
        string? aiVersion = "search-v1", bool badFinalMove = false,
        DateTimeOffset? finishedAt = null)
    {
        var gameId = Guid.NewGuid();
        var moves = new List<MoveRecord>();
        for (var column = 0; column < 4; column++)
        {
            moves.Add(new MoveRecord(moves.Count + 1, 10, column, Stone.X, PlayerActor.Human));
            moves.Add(new MoveRecord(moves.Count + 1, 0, column, Stone.O, PlayerActor.AI,
                "search-v1"));
        }
        moves.Add(new MoveRecord(9, 10, badFinalMove ? 3 : 4, Stone.X, PlayerActor.Human));
        var game = new GameRecord(gameId, 1, Stone.X, GameStatus.XWon,
            GameRecordState.Finished, moves, aiVersion);
        var match = new MatchRecord(matchId, new OwnerIdentity(scope, Guid.NewGuid()),
            RuleSet.CurrentVersion, 20, 3, 0, gameId, 9, [game]);
        return new GuestMatchSource(match,
            new Dictionary<Guid, DateTimeOffset?> { [gameId] = finishedAt ?? Cutoff.AddHours(-1) },
            Cutoff.AddDays(-1));
    }
}
