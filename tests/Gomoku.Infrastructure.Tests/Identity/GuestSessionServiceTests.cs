using System.Security.Cryptography;
using System.Text;
using Gomoku.Infrastructure.Identity;
using Gomoku.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Gomoku.Infrastructure.Tests.Identity;

public sealed class GuestSessionServiceTests
{
    [Fact]
    public async Task TokenResolvesOnlyItsOwnUnexpiredSessionAndExpiredRowRemains()
    {
        var connectionString = Environment.GetEnvironmentVariable("GOMOKU_TEST_POSTGRES")
            ?? throw new InvalidOperationException("Set GOMOKU_TEST_POSTGRES.");
        var options = new DbContextOptionsBuilder<GomokuDbContext>()
            .UseNpgsql(connectionString).Options;
        await using var db = new GomokuDbContext(options);
        await db.Database.MigrateAsync();
        var service = new GuestSessionService(db, TimeProvider.System);

        var first = await service.CreateAsync();
        var second = await service.CreateAsync();
        Assert.NotEqual(first.Token, second.Token);
        Assert.Equal(first.Id, await service.ResolveAsync(first.Token));
        Assert.Equal(second.Id, await service.ResolveAsync(second.Token));
        Assert.Null(await service.ResolveAsync("wrong-token"));

        var row = await db.GuestSessions.SingleAsync(x => x.Id == first.Id);
        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(first.Token)), row.TokenHash);
        row.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();
        Assert.Null(await service.ResolveAsync(first.Token));
        Assert.True(await db.GuestSessions.AnyAsync(x => x.Id == first.Id));
    }
}
