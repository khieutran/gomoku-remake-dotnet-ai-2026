using System.Security.Cryptography;
using Gomoku.Api.Common;
using Gomoku.Api.Identity;
using Gomoku.Application.History;
using Microsoft.AspNetCore.DataProtection;

namespace Gomoku.Api.History;

public static class HistoryEndpoints
{
    public static void MapHistoryEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/history");

        group.MapGet("", async (HttpContext context, CurrentOwnerService owners,
            HistoryService history, IDataProtectionProvider protection) =>
        {
            if (!TryScope(context, out var scope))
                return ValidationError(context, "Scope must be account or guest.");
            if (!TryLimit(context, out var limit))
                return ValidationError(context, "Limit must be between 1 and 50.");
            var encoded = context.Request.Query.TryGetValue("cursor", out var rawCursor)
                ? rawCursor.ToString() : null;
            var protector = protection.CreateProtector("Gomoku.History.Cursor.v1");
            if (!TryCursor(encoded, protector, out var cursor))
                return ValidationError(context, "Cursor is invalid.");

            var owner = await owners.ResolveAsync(context, scope, context.RequestAborted);
            if (owner is null) return AuthenticationError(context);
            var page = await history.ListAsync(owner.Value, cursor, limit, context.RequestAborted);
            return Results.Ok(page.NextCursor is null ? page :
                page with { NextCursor = protector.Protect(page.NextCursor) });
        });

        group.MapGet("/{gameId}", async (HttpContext context, string gameId,
            CurrentOwnerService owners, HistoryService history) =>
        {
            if (!TryScope(context, out var scope))
                return ValidationError(context, "Scope must be account or guest.");
            if (!Guid.TryParse(gameId, out var parsed) || parsed == Guid.Empty)
                return ValidationError(context, "Game ID is invalid.");

            var owner = await owners.ResolveAsync(context, scope, context.RequestAborted);
            if (owner is null) return AuthenticationError(context);
            var game = await history.GetAsync(owner.Value, parsed, context.RequestAborted);
            return game is null
                ? ApiErrors.Create(context, StatusCodes.Status404NotFound,
                    "not_found", "Game was not found.")
                : Results.Ok(game);
        });
    }

    private static bool TryScope(HttpContext context, out string? scope)
    {
        scope = context.Request.Query.TryGetValue("scope", out var raw) ? raw.ToString() : null;
        return scope is null or "account" or "guest";
    }

    private static bool TryLimit(HttpContext context, out int limit)
    {
        limit = 20;
        return !context.Request.Query.TryGetValue("limit", out var raw) ||
            int.TryParse(raw.ToString(), out limit) && limit is >= 1 and <= 50;
    }

    private static bool TryCursor(string? encoded, IDataProtector protector, out HistoryCursor? cursor)
    {
        cursor = null;
        if (encoded is null) return true;
        if (encoded.Length > 1024) return false;
        try
        {
            return HistoryCursor.TryDecode(protector.Unprotect(encoded), out cursor);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static IResult ValidationError(HttpContext context, string message) =>
        ApiErrors.Create(context, StatusCodes.Status400BadRequest, "validation_error", message);

    private static IResult AuthenticationError(HttpContext context) =>
        ApiErrors.Create(context, StatusCodes.Status401Unauthorized,
            "authentication_required", "Sign in or start a guest session to view history.");
}
