using System.Security.Cryptography;
using System.Text;
using Gomoku.Application.Identity;
using Gomoku.Application.Matches;
using Gomoku.Core;
using Xunit;

namespace Gomoku.Application.Tests.Matches;

public sealed class GameFlowServiceRaceTests
{
    [Fact]
    public async Task RetryWhoseFirstReceiptReadMissedReturnsCommittedBody()
    {
        var owner = new OwnerIdentity(OwnerScope.Guest, Guid.NewGuid());
        var matchId = Guid.NewGuid();
        var gameId = Guid.NewGuid();
        var commandId = "same-command";
        var persisted = new MatchRecord(matchId, owner, RuleSet.CurrentVersion, 20,
            0, 0, gameId, 1,
            [new GameRecord(gameId, 1, Stone.X, GameStatus.InProgress, GameRecordState.Active,
                [new MoveRecord(1, 4, 4, Stone.X, PlayerActor.Human)])]);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"move|{matchId:N}|0|4|4|{PlayerActor.Human}")));
        var store = new LateReceiptStore(persisted, commandId, hash);

        var result = await new GameFlowService(store).PlayAsync(owner, matchId,
            4, 4, PlayerActor.Human, 0, commandId);

        Assert.Equal("ok", result.Code);
        Assert.Equal("{\"original\":true}", result.Body);
        Assert.Equal(200, result.HttpStatus);
    }

    private sealed class LateReceiptStore(MatchRecord match, string commandId, string hash) : IGameFlowStore
    {
        private int _reads;

        public Task<MatchRecord?> LoadAsync(OwnerIdentity owner, Guid matchId,
            CancellationToken cancellationToken = default) => Task.FromResult<MatchRecord?>(match);

        public Task<CommandRecord?> FindReceiptAsync(OwnerIdentity owner, string queriedCommand,
            CancellationToken cancellationToken = default)
        {
            _reads++;
            return Task.FromResult<CommandRecord?>(_reads == 1 ? null :
                new CommandRecord(owner, commandId, match.Id, hash, 200,
                    "{\"original\":true}", 1));
        }

        public Task<CommitOutcome> CreateAsync(MatchRecord record, CommandRecord receipt,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<CommitOutcome> CommitAsync(MatchRecord updated, long expectedRevision,
            MoveRecord? addedMove, GameRecord? addedGame, bool completedGame, CommandRecord receipt,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
