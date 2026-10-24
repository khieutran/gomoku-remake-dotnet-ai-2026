using Gomoku.Api.Identity;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();
builder.AddGomokuIdentity();

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
