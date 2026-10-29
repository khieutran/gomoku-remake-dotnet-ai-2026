using Gomoku.Infrastructure.Persistence;
using Npgsql;
using Xunit;

namespace Gomoku.Infrastructure.Tests.Persistence;

public sealed class PostgresConnectionStringTests
{
    [Fact]
    public void PasswordComesFromSecretFile()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "real-secret\n");
            var resolved = PostgresConnectionString.WithPasswordFile(
                "Host=postgres;Database=gomoku;Username=gomoku", path);

            Assert.Equal("real-secret", new NpgsqlConnectionStringBuilder(resolved).Password);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void EmptySecretFailsBeforeDatabaseConnection()
    {
        var path = Path.GetTempFileName();
        try
        {
            Assert.Throws<InvalidOperationException>(() => PostgresConnectionString.WithPasswordFile(
                "Host=postgres;Database=gomoku;Username=gomoku", path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
