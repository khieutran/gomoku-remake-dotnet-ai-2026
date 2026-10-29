using Npgsql;

namespace Gomoku.Infrastructure.Persistence;

public static class PostgresConnectionString
{
    public static string WithPasswordFile(string connectionString, string? passwordFile)
    {
        if (string.IsNullOrWhiteSpace(passwordFile)) return connectionString;

        var password = File.ReadAllText(passwordFile).TrimEnd('\r', '\n');
        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("PostgreSQL password secret is empty.");

        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Password = password };
        return builder.ConnectionString;
    }
}
