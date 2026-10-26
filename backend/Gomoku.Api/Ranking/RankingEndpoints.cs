using Gomoku.Api.Identity;
using Gomoku.Application.Ranking;
using Gomoku.Application.Identity;

namespace Gomoku.Api.Ranking;

public static class RankingEndpoints
{
    public static void MapRankingEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/leaderboard");
        group.MapGet("", async (HttpContext context, RankingService ranking) =>
        {
            var limit = 20;
            if (context.Request.Query.TryGetValue("limit", out var raw) &&
                (!int.TryParse(raw.ToString(), out limit) || limit is < 1 or > 50))
                return ApiErrors.Create(context, StatusCodes.Status400BadRequest,
                    "validation_error", "Limit must be between 1 and 50.");
            return Results.Ok(await ranking.TopAsync(limit, context.RequestAborted));
        });

        group.MapGet("/me", async (HttpContext context, CurrentOwnerService owners,
            RankingService ranking) =>
        {
            var owner = await owners.ResolveAsync(context, "account", context.RequestAborted);
            if (owner is not { Scope: OwnerScope.Account })
                return ApiErrors.Create(context, StatusCodes.Status401Unauthorized,
                    "authentication_required", "Sign in to see your rank.");
            var position = await ranking.MineAsync(owner.Value.Id, context.RequestAborted);
            return position is null
                ? ApiErrors.Create(context, StatusCodes.Status404NotFound,
                    "not_found", "Account was not found.")
                : Results.Ok(position);
        });
    }
}
using Gomoku.Api.Common;
