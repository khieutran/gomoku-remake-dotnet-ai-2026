using System.Text.Json;
using Gomoku.Api.Identity;
using Gomoku.Application.Matches;

namespace Gomoku.Api.Matches;

public static class MatchEndpoints
{
    public static void MapMatchEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/matches");

        group.MapGet("/active", async (HttpContext context, CurrentOwnerService owners,
            ActiveMatchService active) =>
        {
            var scope = GetScope(context);
            if (scope is not null && scope is not ("account" or "guest"))
                return ValidationError(context, "Scope must be account or guest.");
            var limit = 20;
            if (context.Request.Query.TryGetValue("limit", out var rawLimit) &&
                (!int.TryParse(rawLimit.ToString(), out limit) || limit is < 1 or > 50))
                return ValidationError(context, "Limit must be between 1 and 50.");
            var offset = 0;
            if (context.Request.Query.TryGetValue("offset", out var rawOffset) &&
                (!int.TryParse(rawOffset.ToString(), out offset) || offset is < 0 or > 10000))
                return ValidationError(context, "Offset must be between 0 and 10000.");
            var owner = await owners.ResolveAsync(context, scope, context.RequestAborted);
            if (owner is null) return AuthenticationError(context);
            return Results.Ok(await active.ListAsync(owner.Value, offset, limit, context.RequestAborted));
        });

        group.MapPost("", async (HttpContext context, CurrentOwnerService owners,
            GameFlowService gameFlow, ILoggerFactory loggerFactory) =>
        {
            var scope = GetScope(context);
            if (scope is not null && scope is not ("account" or "guest"))
                return ValidationError(context, "Scope must be account or guest.");

            var commandId = await ReadCommandIdAsync(context.Request, context.RequestAborted);
            if (commandId is null)
                return ValidationError(context, "A valid commandId is required.");

            var owner = await owners.ResolveAsync(context, scope, context.RequestAborted);
            if (owner is null) return AuthenticationError(context);

            var result = await gameFlow.CreateMatchAsync(owner.Value, commandId, context.RequestAborted);
            return ToHttpResult(context, result, loggerFactory);
        });

        group.MapGet("/{id}", async (HttpContext context, string id,
            CurrentOwnerService owners, GameFlowService gameFlow, ILoggerFactory loggerFactory) =>
        {
            var scope = GetScope(context);
            if (scope is not null && scope is not ("account" or "guest"))
                return ValidationError(context, "Scope must be account or guest.");
            if (!Guid.TryParse(id, out var matchId) || matchId == Guid.Empty)
                return ValidationError(context, "A valid Match ID is required.");

            var owner = await owners.ResolveAsync(context, scope, context.RequestAborted);
            if (owner is null) return AuthenticationError(context);

            var result = await gameFlow.GetMatchAsync(owner.Value, matchId, context.RequestAborted);
            return ToHttpResult(context, result, loggerFactory);
        });

        group.MapPost("/{id}/moves", async (HttpContext context, string id,
            CurrentOwnerService owners, GameFlowService gameFlow, ILoggerFactory loggerFactory) =>
        {
            var scope = GetScope(context);
            if (scope is not null && scope is not ("account" or "guest"))
                return ValidationError(context, "Scope must be account or guest.");
            if (!Guid.TryParse(id, out var matchId) || matchId == Guid.Empty)
                return ValidationError(context, "A valid Match ID is required.");
            var command = await ReadMoveCommandAsync(context.Request, context.RequestAborted);
            if (command is null)
                return ValidationError(context, "A valid human move is required.");
            var owner = await owners.ResolveAsync(context, scope, context.RequestAborted);
            if (owner is null) return AuthenticationError(context);
            var result = await gameFlow.PlayAsync(owner.Value, matchId, command.Value.Row,
                command.Value.Column, PlayerActor.Human, command.Value.ExpectedRevision,
                command.Value.CommandId, context.RequestAborted);
            return ToHttpResult(context, result, loggerFactory);
        });

        group.MapPost("/{id}/next-game", async (HttpContext context, string id,
            CurrentOwnerService owners, GameFlowService gameFlow, ILoggerFactory loggerFactory) =>
        {
            var scope = GetScope(context);
            if (scope is not null && scope is not ("account" or "guest"))
                return ValidationError(context, "Scope must be account or guest.");
            if (!Guid.TryParse(id, out var matchId) || matchId == Guid.Empty)
                return ValidationError(context, "A valid Match ID is required.");
            var command = await ReadNextGameCommandAsync(context.Request, context.RequestAborted);
            if (command is null)
                return ValidationError(context, "A valid next-game command is required.");
            var owner = await owners.ResolveAsync(context, scope, context.RequestAborted);
            if (owner is null) return AuthenticationError(context);
            var result = await gameFlow.StartNextGameAsync(owner.Value, matchId,
                command.Value.ExpectedRevision, command.Value.CommandId, context.RequestAborted);
            return ToHttpResult(context, result, loggerFactory);
        });
    }

    private static string? GetScope(HttpContext context) =>
        context.Request.Query.TryGetValue("scope", out var scope) ? scope.ToString() : null;

    private static async Task<string?> ReadCommandIdAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                document.RootElement.EnumerateObject().Count() != 1 ||
                !document.RootElement.TryGetProperty("commandId", out var element) ||
                element.ValueKind != JsonValueKind.String)
                return null;

            var commandId = element.GetString();
            return ValidPublicCommandId(commandId) ? commandId : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<(string CommandId, int Row, int Column, long ExpectedRevision)?>
        ReadMoveCommandAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 4 ||
                !TryCommandId(root, out var commandId) ||
                !root.TryGetProperty("row", out var row) || row.ValueKind != JsonValueKind.Number ||
                !row.TryGetInt32(out var rowValue) ||
                !root.TryGetProperty("column", out var column) || column.ValueKind != JsonValueKind.Number ||
                !column.TryGetInt32(out var columnValue) ||
                !TryRevision(root, out var revision))
                return null;
            return (commandId!, rowValue, columnValue, revision);
        }
        catch (JsonException) { return null; }
    }

    private static async Task<(string CommandId, long ExpectedRevision)?>
        ReadNextGameCommandAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 2 ||
                !TryCommandId(root, out var commandId) || !TryRevision(root, out var revision))
                return null;
            return (commandId!, revision);
        }
        catch (JsonException) { return null; }
    }

    private static bool TryCommandId(JsonElement root, out string? commandId)
    {
        commandId = null;
        if (!root.TryGetProperty("commandId", out var element) || element.ValueKind != JsonValueKind.String)
            return false;
        commandId = element.GetString();
        return ValidPublicCommandId(commandId);
    }

    private static bool ValidPublicCommandId(string? commandId) =>
        !string.IsNullOrWhiteSpace(commandId) && commandId.Length <= 128 &&
        !commandId.StartsWith("ai:", StringComparison.OrdinalIgnoreCase);

    private static bool TryRevision(JsonElement root, out long revision)
    {
        revision = 0;
        return root.TryGetProperty("expectedRevision", out var element) &&
            element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out revision) &&
            revision >= 0;
    }

    private static IResult ToHttpResult(HttpContext context, FlowResult result, ILoggerFactory loggerFactory)
    {
        if (result.Code == "ok")
            return Results.Content(result.Body, "application/json", statusCode: result.HttpStatus);

        if (result.Code == "state_integrity_error")
            loggerFactory.CreateLogger("Gomoku.MatchIntegrity")
                .LogError("Stored Match failed replay integrity validation, trace {TraceId}", context.TraceIdentifier);

        var (status, message) = result.Code switch
        {
            "validation_error" => (StatusCodes.Status400BadRequest, "Match request is invalid."),
            "not_found" => (StatusCodes.Status404NotFound, "Match was not found."),
            "idempotency_conflict" => (StatusCodes.Status409Conflict, "Command ID was used for another request."),
            "stale_revision" => (StatusCodes.Status409Conflict, "Match revision has changed."),
            "occupied" => (StatusCodes.Status409Conflict, "Board cell is occupied."),
            "wrong_turn" => (StatusCodes.Status409Conflict, "It is not the human player's turn."),
            "game_finished" => (StatusCodes.Status409Conflict, "Game has finished."),
            "game_in_progress" => (StatusCodes.Status409Conflict, "Current Game is still in progress."),
            "invalid_move" => (StatusCodes.Status400BadRequest, "Move is outside the board."),
            "unsupported_rule_version" => (StatusCodes.Status409Conflict, "Match rule version is unsupported."),
            "state_integrity_error" => (StatusCodes.Status500InternalServerError, "Match data cannot be loaded."),
            _ => (StatusCodes.Status500InternalServerError, "Match request could not be completed.")
        };
        return ApiErrors.Create(context, status, result.Code, message);
    }

    private static IResult ValidationError(HttpContext context, string message) =>
        ApiErrors.Create(context, StatusCodes.Status400BadRequest, "validation_error", message);

    private static IResult AuthenticationError(HttpContext context) =>
        ApiErrors.Create(context, StatusCodes.Status401Unauthorized,
            "authentication_required", "Sign in or start a guest session to access matches.");
}
