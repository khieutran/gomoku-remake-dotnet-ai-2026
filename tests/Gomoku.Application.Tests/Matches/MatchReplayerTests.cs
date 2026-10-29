using Gomoku.Application.Identity;
using Gomoku.Application.Matches;
using Gomoku.Core;
using Xunit;

namespace Gomoku.Application.Tests.Matches;

public sealed class MatchReplayerTests
{
    private static MatchRecord Initial() => new(
        Guid.NewGuid(), new OwnerIdentity(OwnerScope.Guest, Guid.NewGuid()),
        RuleSet.CurrentVersion, 20, 0, 0, Guid.NewGuid(), 0,
        [new GameRecord(Guid.NewGuid(), 1, Stone.X, GameStatus.InProgress,
            GameRecordState.Active, [])]);

    [Fact]
    public void ReplaysStoredMovesThroughCore()
    {
        var initial = Initial();
        var game = initial.Games[0] with
        {
            Moves =
            [
                new MoveRecord(1, 4, 4, Stone.X, PlayerActor.Human),
                new MoveRecord(2, 4, 5, Stone.O, PlayerActor.AI)
            ]
        };
        var record = initial with { CurrentGameId = game.Id, Revision = 2, Games = [game] };
        var snapshot = MatchReplayer.Replay(record).Snapshot();
        Assert.Equal(2, snapshot.CurrentGame.MoveIndex);
        Assert.Equal(Stone.X, snapshot.CurrentGame.NextStone);
    }

    [Fact]
    public void ReplaysHumanOAndAiXWithoutSwappingActors()
    {
        var initial = Initial();
        var game = initial.Games[0] with
        {
            OpeningStone = Stone.O,
            Moves =
            [
                new MoveRecord(1, 4, 4, Stone.O, PlayerActor.Human),
                new MoveRecord(2, 4, 5, Stone.X, PlayerActor.AI)
            ]
        };
        var record = initial with
        {
            HumanStone = Stone.O, CurrentGameId = game.Id, Revision = 2, Games = [game]
        };
        Assert.Equal(Stone.O, MatchReplayer.Replay(record).Snapshot().CurrentGame.NextStone);
        var wrong = game with { Moves = [game.Moves[0] with { Actor = PlayerActor.AI }, game.Moves[1]] };
        Assert.Throws<StateIntegrityException>(() => MatchReplayer.Replay(record with { Games = [wrong] }));
    }

    [Fact]
    public void RejectsMissingStepAndWrongStone()
    {
        var initial = Initial();
        var game = initial.Games[0] with
        {
            Moves = [new MoveRecord(2, 4, 4, Stone.O, PlayerActor.AI)]
        };
        var record = initial with { CurrentGameId = game.Id, Revision = 1, Games = [game] };
        Assert.Throws<StateIntegrityException>(() => MatchReplayer.Replay(record));
    }

    [Fact]
    public void RejectsUnknownRuleInsteadOfReplayingWithCurrentRules()
    {
        var initial = Initial();
        var record = initial with { RuleVersion = "future-rule", CurrentGameId = initial.Games[0].Id };
        Assert.Throws<UnsupportedRuleVersionException>(() => MatchReplayer.Replay(record));
    }

    [Theory]
    [InlineData(30)]
    [InlineData(40)]
    public void ReplaysSelectedLargeBoard(int boardSize)
    {
        var initial = Initial();
        var record = initial with { BoardSize = boardSize, CurrentGameId = initial.Games[0].Id };
        Assert.Equal(boardSize, MatchReplayer.Replay(record).Snapshot().CurrentGame.Board.Size);
    }
}
