using System.Security.Claims;
using Gomoku.Api.Identity;
using Gomoku.Application.Identity;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Gomoku.Api.Tests.Identity;

public sealed class CurrentOwnerServiceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("account")]
    public async Task AccountSelectionSkipsGuestLookup(string? scope)
    {
        var accountId = Guid.NewGuid();
        var guests = new CountingGuests(Guid.NewGuid());
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, accountId.ToString())], "test"));
        context.Request.Headers.Cookie = "gomoku.guest=unused";

        var owner = await new CurrentOwnerService(guests).ResolveAsync(context, scope);

        Assert.Equal(new OwnerIdentity(OwnerScope.Account, accountId), owner);
        Assert.Equal(0, guests.ResolveCount);
    }

    [Fact]
    public async Task ExplicitGuestSelectionResolvesGuestWhenSignedIn()
    {
        var guestId = Guid.NewGuid();
        var guests = new CountingGuests(guestId);
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "test"));

        var owner = await new CurrentOwnerService(guests).ResolveAsync(context, "guest");

        Assert.Equal(new OwnerIdentity(OwnerScope.Guest, guestId), owner);
        Assert.Equal(1, guests.ResolveCount);
    }

    private sealed class CountingGuests(Guid? resolvedId) : IGuestSessionService
    {
        public int ResolveCount { get; private set; }
        public Task<CreatedGuestSession> CreateAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<Guid?> ResolveAsync(string? token, CancellationToken cancellationToken = default)
        {
            ResolveCount++;
            return Task.FromResult(resolvedId);
        }
    }
}
