using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Gomoku.Infrastructure.Persistence;

public sealed class GomokuDbContextFactory : IDesignTimeDbContextFactory<GomokuDbContext>
{
    public GomokuDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("GOMOKU_POSTGRES_CONNECTION")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__Gomoku")
            ?? "Host=127.0.0.1;Port=55432;Database=gomoku;Username=gomoku;Password=gomoku_dev";
        connectionString = PostgresConnectionString.WithPasswordFile(
            connectionString, Environment.GetEnvironmentVariable("GOMOKU_POSTGRES_PASSWORD_FILE"));

        var options = new DbContextOptionsBuilder<GomokuDbContext>()
            .UseNpgsql(connectionString).Options;
        return new GomokuDbContext(options);
    }
}
