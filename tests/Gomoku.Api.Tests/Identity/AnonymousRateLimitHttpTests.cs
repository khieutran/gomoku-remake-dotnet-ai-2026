using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gomoku.Application.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Gomoku.Api.Tests.Identity;

public sealed class AnonymousRateLimitHttpTests
{
    private static WebApplicationFactory<Program> OpenFactory(Action<IWebHostBuilder>? configure = null)
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Gomoku", "Host=127.0.0.1;Port=1;Database=unused;Username=unused");
            builder.UseSetting("GOMOKU_DP_KEYS_DIR", Path.Combine(Path.GetTempPath(), "gomoku-rate-test-" + Guid.NewGuid().ToString("N")));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IGuestSessionService>();
                services.AddSingleton<IGuestSessionService, MemoryGuests>();
                services.AddTransient<IStartupFilter, TestRemoteIpStartupFilter>();
            });
            configure?.Invoke(builder);
        });
        return factory;
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object? body = null)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("requestToken").GetString());
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task GuestLimitIsSharedByClientsOnOneIpAndReturnsRetryAfter()
    {
        using var factory = OpenFactory();
        // Each client gets its own cookie jar. The shared connection IP must still share the limit.
        for (var i = 0; i < 10; i++)
        {
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Remote-IP", "192.0.2.10");
            using var response = await PostAsync(client, "/api/v1/guest-sessions");
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
        using var anotherClient = factory.CreateClient();
        anotherClient.DefaultRequestHeaders.Add("X-Test-Remote-IP", "192.0.2.10");
        using var limited = await PostAsync(anotherClient, "/api/v1/guest-sessions");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.RetryAfter?.Delta > TimeSpan.Zero);
    }

    [Fact]
    public async Task ExistingGuestCookieKeepsItsSessionWithinLimit()
    {
        using var factory = OpenFactory();
        using var client = factory.CreateClient();
        using var created = await PostAsync(client, "/api/v1/guest-sessions");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("guestSessionId").GetGuid();
        using var reused = await PostAsync(client, "/api/v1/guest-sessions");
        Assert.Equal(HttpStatusCode.OK, reused.StatusCode);
        Assert.Equal(id, (await reused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("guestSessionId").GetGuid());
    }

    [Theory]
    [InlineData("/api/v1/auth/register", 5)]
    [InlineData("/api/v1/auth/login", 20)]
    public async Task AuthRoutesHaveIndependentDefaultLimits(string path, int allowance)
    {
        using var factory = OpenFactory();
        using var client = factory.CreateClient();
        for (var i = 0; i < allowance; i++)
        {
            using var response = await PostAsync(client, path, new { email = "", password = "" });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        using var limited = await PostAsync(client, path, new { email = "", password = "" });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.RetryAfter?.Delta > TimeSpan.Zero);
    }

    [Fact]
    public async Task SpoofedForwardedForDoesNotChangeUntrustedClientIp()
    {
        using var factory = OpenFactory(builder => builder.UseSetting("RateLimiting:GuestSession:PermitLimit", "1"));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Remote-IP", "192.0.2.11");
        using var first = await PostAsync(client, "/api/v1/guest-sessions");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100.1");
        using var second = await PostAsync(client, "/api/v1/guest-sessions");
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }

    [Fact]
    public async Task ConfiguredProxyUsesForwardedClientIp()
    {
        using var factory = OpenFactory(builder =>
        {
            builder.UseSetting("RateLimiting:GuestSession:PermitLimit", "1");
            builder.UseSetting("RateLimiting:KnownProxies:0", "127.0.0.1");
        });
        using var first = factory.CreateClient();
        first.DefaultRequestHeaders.Add("X-Test-Remote-IP", "127.0.0.1");
        first.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100.1");
        using var firstResponse = await PostAsync(first, "/api/v1/guest-sessions");
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        using var second = factory.CreateClient();
        second.DefaultRequestHeaders.Add("X-Test-Remote-IP", "127.0.0.1");
        second.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100.2");
        using var secondResponse = await PostAsync(second, "/api/v1/guest-sessions");
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
    }

    [Fact]
    public async Task ConfiguredProxyUsesForwardedHttpsScheme()
    {
        using var factory = OpenFactory(builder =>
            builder.UseSetting("RateLimiting:KnownProxies:0", "127.0.0.1"));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Remote-IP", "127.0.0.1");
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");

        using var response = await client.GetAsync("/api/v1/auth/csrf");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), value =>
            value.StartsWith("gomoku.csrf=") && value.Contains("secure", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task UntrustedClientCannotSpoofForwardedHttpsScheme()
    {
        using var factory = OpenFactory(builder =>
            builder.UseSetting("RateLimiting:KnownProxies:0", "127.0.0.1"));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Remote-IP", "192.0.2.11");
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");

        using var response = await client.GetAsync("/api/v1/auth/csrf");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), value =>
            value.StartsWith("gomoku.csrf=") && !value.Contains("secure", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class TestRemoteIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, continuation) =>
            {
                if (context.Request.Headers.TryGetValue("X-Test-Remote-IP", out var value))
                    context.Connection.RemoteIpAddress = IPAddress.Parse(value.ToString());
                return continuation(context);
            });
            next(app);
        };
    }

    private sealed class MemoryGuests : IGuestSessionService
    {
        private readonly ConcurrentDictionary<string, Guid> _sessions = new();

        public Task<CreatedGuestSession> CreateAsync(CancellationToken cancellationToken = default)
        {
            var token = Guid.NewGuid().ToString("N");
            var id = Guid.NewGuid();
            _sessions[token] = id;
            return Task.FromResult(new CreatedGuestSession(id, token, DateTimeOffset.UtcNow.AddDays(30)));
        }

        public Task<Guid?> ResolveAsync(string? token, CancellationToken cancellationToken = default) =>
            Task.FromResult(token is not null && _sessions.TryGetValue(token, out var id) ? (Guid?)id : null);
    }
}
