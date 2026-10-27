using Gomoku.Api.Common;
using Gomoku.Api.Identity;
using Gomoku.Api.History;
using Gomoku.Api.Health;
using Gomoku.Api.Matches;
using Gomoku.Api.Ranking;
using Gomoku.Api.Redis;
using System.Text.RegularExpressions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks()
    .AddCheck<PostgreSqlReadyHealthCheck>("postgresql", tags: ["ready"],
        timeout: TimeSpan.FromSeconds(2));
builder.AddAnonymousRateLimiting();
builder.AddGomokuRedisKeyspace();
builder.AddGomokuIdentity();
builder.AddGomokuGameFlow();
builder.AddGomokuHistory();
builder.AddGomokuRanking();

var app = builder.Build();

app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Content-Security-Policy"] =
            "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; " +
            "font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'; form-action 'self'";
        if (context.Request.Path.StartsWithSegments("/api") ||
            context.Request.Path.StartsWithSegments("/health") ||
            context.Request.Path.StartsWithSegments("/ready"))
        {
            headers["Cache-Control"] = "private, no-store";
        }
        return Task.CompletedTask;
    });
    await next(context);
});

app.UseStorageFailureHandler();
app.UseKnownProxyForwarding();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        var name = Path.GetFileName(context.File.Name);
        context.Context.Response.Headers.CacheControl = name.Equals("index.html", StringComparison.OrdinalIgnoreCase)
            ? "no-cache"
            : Regex.IsMatch(name, @"-[a-zA-Z0-9_-]{8,}\.(js|css)$", RegexOptions.IgnoreCase)
                ? "public, max-age=31536000, immutable"
                : "no-cache";
    }
});
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseApiAntiforgery();
app.UseRateLimiter();

app.MapHealthChecks("/health", new() { Predicate = _ => false });
app.MapHealthChecks("/ready", new() { Predicate = check => check.Tags.Contains("ready") });
app.MapAuthEndpoints();
app.MapGuestSessionEndpoints();
app.MapMatchEndpoints();
app.MapHistoryEndpoints();
app.MapRankingEndpoints();

app.MapFallback("{**path}", async context =>
{
    var path = context.Request.Path.Value ?? "";
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsJsonAsync(new
        {
            code = "not_found",
            message = "API endpoint not found.",
            traceId = context.TraceIdentifier
        });
        return;
    }

    var frontendRoute = path is "/" or "/play" or "/login" or "/register" or "/history" or "/leaderboard"
        || path.StartsWith("/play/", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/history/", StringComparison.OrdinalIgnoreCase);
    if (!frontendRoute || !(HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    var index = app.Environment.WebRootFileProvider.GetFileInfo("index.html");
    if (!index.Exists)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }
    context.Response.Headers.CacheControl = "no-cache";
    context.Response.ContentType = "text/html; charset=utf-8";
    context.Response.ContentLength = index.Length;
    if (HttpMethods.IsGet(context.Request.Method))
    {
        await context.Response.SendFileAsync(index);
    }
});

app.Run();

public partial class Program;
