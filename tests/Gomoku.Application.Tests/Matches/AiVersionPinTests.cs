using Gomoku.Application.Identity;
using Gomoku.Application.Matches;
using Gomoku.Core;
using System.Text.Json;

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

    [Theory]
    [InlineData(Stone.X, true, Stone.X, "human")]
    [InlineData(Stone.X, false, Stone.O, "ai_pending")]
    [InlineData(Stone.O, true, Stone.O, "human")]
    [InlineData(Stone.O, false, Stone.X, "ai_pending")]
    public async Task CreationKeepsHumanStoneAndOpeningOrder(
        Stone humanStone, bool humanFirst, Stone openingStone, string turnStatus)
    {
        var owner = new OwnerIdentity(OwnerScope.Guest, Guid.NewGuid());
        var store = new CaptureStore();
        var created = await new GameFlowService(store).CreateMatchAsync(owner, Guid.NewGuid().ToString(),
            humanStone: humanStone, humanFirst: humanFirst);

        Assert.Equal("ok", created.Code);
        Assert.Equal(humanStone, store.Created!.HumanStone);
        Assert.Equal(openingStone, store.Created.Games[0].OpeningStone);
        using var body = JsonDocument.Parse(created.Body!);
        Assert.Equal(humanStone.ToString(), body.RootElement.GetProperty("humanStone").GetString());
        Assert.Equal(turnStatus, body.RootElement.GetProperty("currentGame")
            .GetProperty("turnStatus").GetString());
    }

    [Theory]
    [InlineData(20)]
    [InlineData(30)]
    [InlineData(40)]
    public async Task CreationPersistsSelectedBoardSize(int boardSize)
    {
        var owner = new OwnerIdentity(OwnerScope.Guest, Guid.NewGuid());
        var store = new CaptureStore();
        var result = await new GameFlowService(store).CreateMatchAsync(owner, Guid.NewGuid().ToString(),
            boardSize: boardSize);
        Assert.Equal("ok", result.Code);
        Assert.Equal(boardSize, store.Created!.BoardSize);
        using var body = JsonDocument.Parse(result.Body!);
        Assert.Equal(boardSize, body.RootElement.GetProperty("boardSize").GetInt32());
    }

    [Fact]
    public async Task RectangularMatchPinsSearchAndReturnsBothDimensions()
    {
        var owner = new OwnerIdentity(OwnerScope.Guest, Guid.NewGuid());
        var store = new CaptureStore();
        var result = await new GameFlowService(store).CreateMatchAsync(owner, Guid.NewGuid().ToString(),
            boardSize: 20, boardColumns: 30);

        Assert.Equal("ok", result.Code);
        Assert.Equal(30, store.Created!.Columns);
        Assert.Equal(AiVersions.SearchV1, store.Created.Games[0].AiVersion);
        Assert.Equal(30, MatchReplayer.Replay(store.Created).Snapshot().CurrentGame.Board.Columns);
        using var body = JsonDocument.Parse(result.Body!);
        Assert.Equal(30, body.RootElement.GetProperty("boardColumns").GetInt32());
    }

    [Fact]
    public async Task CreationRejectsUnsupportedBoardSize()
    {
        var store = new CaptureStore();
        var result = await new GameFlowService(store).CreateMatchAsync(
            new OwnerIdentity(OwnerScope.Guest, Guid.NewGuid()), Guid.NewGuid().ToString(), boardSize: 25);
        Assert.Equal("validation_error", result.Code);
        Assert.Null(store.Created);
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
