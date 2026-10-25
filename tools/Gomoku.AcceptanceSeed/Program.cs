using Gomoku.Application.Identity;
using Gomoku.Application.Matches;
using Gomoku.Infrastructure.Matches;
using Gomoku.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

if (args.Length != 4 || !Guid.TryParse(args[0], out var accountId) ||
    !Guid.TryParse(args[1], out var accountMatchId) ||
    !Guid.TryParse(args[2], out var guestId) ||
    !Guid.TryParse(args[3], out var guestMatchId))
    throw new ArgumentException("Expected accountId accountMatchId guestId guestMatchId.");

var connectionString = Environment.GetEnvironmentVariable("GOMOKU_ACCEPTANCE_POSTGRES")
    ?? throw new InvalidOperationException("GOMOKU_ACCEPTANCE_POSTGRES is required.");
await using var db = new GomokuDbContext(new DbContextOptionsBuilder<GomokuDbContext>()
    .UseNpgsql(connectionString).Options);
var flow = new GameFlowService(new EfGameFlowStore(db, TimeProvider.System));

await FinishXAsync(new OwnerIdentity(OwnerScope.Account, accountId), accountMatchId);
await FinishXAsync(new OwnerIdentity(OwnerScope.Guest, guestId), guestMatchId);
Console.WriteLine("PASS: seeded finished account and guest games through GameFlowService");

async Task FinishXAsync(OwnerIdentity owner, Guid matchId)
{
    long revision = 0;
    for (var column = 0; column < 5; column++)
    {
        await PlayAsync(owner, matchId, 0, column, PlayerActor.Human, revision++);
        if (column < 4)
            await PlayAsync(owner, matchId, 1, column, PlayerActor.AI, revision++);
    }
}

async Task PlayAsync(OwnerIdentity owner, Guid matchId, int row, int column,
    PlayerActor actor, long revision)
{
    var result = await flow.PlayAsync(owner, matchId, row, column, actor, revision,
        "acceptance-" + Guid.NewGuid());
    if (result.Code != "ok")
        throw new InvalidOperationException($"Move {revision} failed: {result.Code}");
}
