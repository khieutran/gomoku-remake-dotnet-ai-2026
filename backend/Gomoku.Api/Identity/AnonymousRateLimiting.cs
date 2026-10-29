using System.Net;
using System.Threading.RateLimiting;
using Gomoku.Api.Common;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;

namespace Gomoku.Api.Identity;

public static class AnonymousRateLimiting
{
    public const string GuestSession = "guest-session";
    public const string Register = "register";
    public const string Login = "login";

    public static void AddAnonymousRateLimiting(this WebApplicationBuilder builder)
    {
        var settings = builder.Configuration.GetSection("RateLimiting").Get<Settings>() ?? new Settings();
        builder.Services.AddRateLimiter(options =>
        {
            AddPolicy(options, GuestSession, settings.GuestSession);
            AddPolicy(options, Register, settings.Register);
            AddPolicy(options, Login, settings.Login);
            options.OnRejected = async (rejection, cancellationToken) =>
            {
                var response = rejection.HttpContext.Response;
                response.StatusCode = StatusCodes.Status429TooManyRequests;
                if (rejection.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
                {
                    response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
                }
                await ApiErrors.Create(rejection.HttpContext, StatusCodes.Status429TooManyRequests,
                    "rate_limit_exceeded", "Too many requests. Try again later.")
                    .ExecuteAsync(rejection.HttpContext);
            };
        });

        var proxyAddresses = builder.Configuration.GetSection("RateLimiting:KnownProxies").Get<string[]>() ?? [];
        if (proxyAddresses.Length == 0) return;

        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (var address in proxyAddresses)
            {
                if (!IPAddress.TryParse(address, out var parsed))
                    throw new InvalidOperationException($"Invalid RateLimiting:KnownProxies address: {address}");
                options.KnownProxies.Add(parsed);
            }
        });
    }

    public static void UseKnownProxyForwarding(this WebApplication app)
    {
        if (app.Configuration.GetSection("RateLimiting:KnownProxies").Get<string[]>() is { Length: > 0 })
            app.UseForwardedHeaders();
    }

    private static void AddPolicy(RateLimiterOptions options, string name, Limit limit)
    {
        if (limit.PermitLimit <= 0 || limit.WindowMinutes <= 0)
            throw new InvalidOperationException($"RateLimiting:{name} requires positive PermitLimit and WindowMinutes.");

        options.AddPolicy(name, context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.MapToIPv6().ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limit.PermitLimit,
                Window = TimeSpan.FromMinutes(limit.WindowMinutes),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    }

    public sealed class Settings
    {
        public Limit GuestSession { get; set; } = new(10, 60);
        public Limit Register { get; set; } = new(5, 60);
        public Limit Login { get; set; } = new(20, 15);
    }

    public sealed class Limit(int permitLimit, int windowMinutes)
    {
        public int PermitLimit { get; set; } = permitLimit;
        public int WindowMinutes { get; set; } = windowMinutes;
    }
}
