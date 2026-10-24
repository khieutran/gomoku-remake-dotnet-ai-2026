using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Gomoku.Api.Identity;

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
            if (current is DbUpdateException or NpgsqlException or TimeoutException)
            {
                return true;
            }
        }
        return false;
    }
}
