using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Net.Sockets;

namespace Gomoku.Api.Common;

public static class StorageFailureHandler
{
    public static IApplicationBuilder UseStorageFailureHandler(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            try
            {
                await next(context);
            }
            catch (Exception exception) when (IsStorageFailure(exception))
            {
                if (context.Response.HasStarted)
                {
                    throw;
                }

                context.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Gomoku.Storage")
                    .LogError(exception, "PostgreSQL operation failed");
                context.Response.Clear();
                await ApiErrors.Create(context, StatusCodes.Status503ServiceUnavailable,
                    "storage_unavailable", "Storage is temporarily unavailable.").ExecuteAsync(context);
            }
        });

    private static bool IsStorageFailure(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is TimeoutException or SocketException ||
                current is NpgsqlException { IsTransient: true })
            {
                return true;
            }
        }
        return false;
    }
}
