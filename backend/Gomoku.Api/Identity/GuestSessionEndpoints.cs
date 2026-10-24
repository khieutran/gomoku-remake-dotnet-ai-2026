using Gomoku.Application.Identity;

namespace Gomoku.Api.Identity;

public static class GuestSessionEndpoints
{
    public const string CookieName = "gomoku.guest";

    public static void MapGuestSessionEndpoints(this WebApplication app)
    {
        app.MapPost("/api/v1/guest-sessions", async (HttpContext context,
            IGuestSessionService guests, IWebHostEnvironment environment) =>
        {
            var existingToken = context.Request.Cookies[CookieName];
            var existingId = await guests.ResolveAsync(existingToken, context.RequestAborted);
            if (existingId.HasValue)
            {
                return Results.Ok(new { guestSessionId = existingId.Value });
            }

            var created = await guests.CreateAsync(context.RequestAborted);
            context.Response.Cookies.Append(CookieName, created.Token, new CookieOptions
            {
                HttpOnly = true,
                Secure = !environment.IsDevelopment(),
                SameSite = SameSiteMode.Lax,
                Expires = created.ExpiresAt,
                IsEssential = true
            });
            return Results.Created("/api/v1/guest-sessions", new
            {
                guestSessionId = created.Id,
                expiresAt = created.ExpiresAt
            });
        });
    }
}
