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
}
