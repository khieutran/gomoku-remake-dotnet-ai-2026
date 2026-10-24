using Gomoku.Api.Identity;
using Gomoku.Api.Matches;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();
builder.AddGomokuIdentity();
builder.AddGomokuGameFlow();

var app = builder.Build();

app.UseStorageFailureHandler();
app.UseAuthentication();
app.UseAuthorization();
app.UseApiAntiforgery();

app.MapHealthChecks("/health");
app.MapAuthEndpoints();
app.MapGuestSessionEndpoints();

app.Run();

public partial class Program;
