namespace Gomoku.Api.Identity;

public static class ApiErrors
{
    public static IResult Create(HttpContext context, int status, string code, string message,
        string[]? errors = null) => Results.Json(new
    {
        code,
        message,
        traceId = context.TraceIdentifier,
        errors
    }, statusCode: status);
}
