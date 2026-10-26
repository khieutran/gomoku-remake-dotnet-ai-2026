using Gomoku.Application.Identity;
using Gomoku.Application.Matches;

namespace Gomoku.Application.Tests.Matches;

public sealed class AiVersionPinTests
{
    [Fact]
    public async Task NewGamePinsProviderVersionAtCreation()
    {
        var owner = new OwnerIdentity(OwnerScope.Guest, Guid.NewGuid());
        var store = new CaptureStore();
        var champion = new MutableVersion { CurrentVersion = "policy-v1-approved" };
        var flow = new GameFlowService(store, null, champion);

        var created = await flow.CreateMatchAsync(owner, "create-command");
        champion.CurrentVersion = "search-v1";

        Assert.Equal("ok", created.Code);
        Assert.Equal("policy-v1-approved", store.Created!.Games[0].AiVersion);
    }

    private sealed class MutableVersion : IAiVersionProvider
    {
        public string CurrentVersion { get; set; } = AiVersions.Current;
    }

    private sealed class CaptureStore : IGameFlowStore
    {
        public MatchRecord? Created { get; private set; }

        public Task<MatchRecord?> LoadAsync(OwnerIdentity owner, Guid matchId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<MatchRecord?>(Created);

        public Task<CommandRecord?> FindReceiptAsync(OwnerIdentity owner, string commandId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<CommandRecord?>(null);

        public Task<CommitOutcome> CreateAsync(MatchRecord match, CommandRecord receipt,
            CancellationToken cancellationToken = default)
        {
            Created = match;
            return Task.FromResult(CommitOutcome.Committed);
        }

        public Task<CommitOutcome> CommitAsync(MatchRecord updated, long expectedRevision,
            MoveRecord? addedMove, GameRecord? addedGame, bool completedGame,
            CommandRecord receipt, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
