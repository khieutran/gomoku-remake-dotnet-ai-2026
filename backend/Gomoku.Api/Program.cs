using Gomoku.Api.Identity;
using Gomoku.Api.History;
using Gomoku.Api.Matches;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();
builder.AddGomokuIdentity();
builder.AddGomokuGameFlow();
builder.AddGomokuHistory();

var app = builder.Build();

app.UseStorageFailureHandler();
app.UseAuthentication();
app.UseAuthorization();
app.UseApiAntiforgery();

app.MapHealthChecks("/health");
app.MapAuthEndpoints();
app.MapGuestSessionEndpoints();
app.MapMatchEndpoints();
app.MapHistoryEndpoints();

app.Run();

public partial class Program;
