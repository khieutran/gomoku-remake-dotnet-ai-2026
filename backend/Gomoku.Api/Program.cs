using Gomoku.Api.Identity;
using Gomoku.Api.History;
using Gomoku.Api.Health;
using Gomoku.Api.Matches;
using Gomoku.Api.Ranking;
using Gomoku.Api.Redis;

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

app.UseStorageFailureHandler();
app.UseKnownProxyForwarding();
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

app.Run();

public partial class Program;
