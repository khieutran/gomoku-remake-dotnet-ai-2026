using System.Security.Claims;
using Gomoku.Application.Identity;

namespace Gomoku.Api.Identity;

public sealed class CurrentOwnerService(IGuestSessionService guests)
{
    public async Task<OwnerIdentity?> ResolveAsync(
        HttpContext context, string? scope, CancellationToken cancellationToken = default)
    {
        var accountClaim = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        Guid? accountId = Guid.TryParse(accountClaim, out var parsed) ? parsed : null;
        if (scope == "account" || scope is null && accountId.HasValue)
            return OwnerSelector.Select(accountId, null, scope);

        var guestToken = context.Request.Cookies[GuestSessionEndpoints.CookieName];
        var guestId = await guests.ResolveAsync(guestToken, cancellationToken);
        return OwnerSelector.Select(accountId, guestId, scope);
    }
}
