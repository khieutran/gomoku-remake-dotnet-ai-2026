using Gomoku.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();
var connectionString = builder.Configuration.GetConnectionString("Gomoku");
if (!string.IsNullOrWhiteSpace(connectionString))
{
    builder.Services.AddDbContext<GomokuDbContext>(options => options.UseNpgsql(connectionString));
}

var app = builder.Build();

app.MapHealthChecks("/health");

app.Run();
