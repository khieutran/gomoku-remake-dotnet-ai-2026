using Microsoft.AspNetCore.Antiforgery;

namespace Gomoku.Api.Identity;

public static class AntiforgeryGuard
{
    public static IApplicationBuilder UseApiAntiforgery(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        if (context.Request.Path.StartsWithSegments("/api/v1") &&
            !HttpMethods.IsGet(context.Request.Method) &&
            !HttpMethods.IsHead(context.Request.Method) &&
            !HttpMethods.IsOptions(context.Request.Method))
        {
            var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
            if (!await antiforgery.IsRequestValidAsync(context))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new
                {
                    code = "csrf_invalid",
                    message = "CSRF token is missing or invalid.",
                    traceId = context.TraceIdentifier
                });
                return;
            }
        }
        await next(context);
    });
}
