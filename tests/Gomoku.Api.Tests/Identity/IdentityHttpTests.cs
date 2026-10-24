using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Gomoku.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Gomoku.Api.Tests.Identity;

public sealed class IdentityHttpTests
{
    private static WebApplicationFactory<Program> OpenFactory()
    {
        var connectionString = Environment.GetEnvironmentVariable("GOMOKU_TEST_POSTGRES")
            ?? throw new InvalidOperationException("Set GOMOKU_TEST_POSTGRES.");
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Gomoku", connectionString));
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object? body = null)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        var token = csrf.GetProperty("requestToken").GetString();
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("X-CSRF-TOKEN", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task RegisterLoginAndLogoutRecoverTheSameAccount()
    {
        using var factory = OpenFactory();
        using var client = factory.CreateClient();
        var email = $"task3-{Guid.NewGuid():N}@example.test";
        using var register = await PostAsync(client, "/api/v1/auth/register",
            new { email, password = "StrongPass!123" });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        var registered = await register.Content.ReadFromJsonAsync<JsonElement>();
        var userId = registered.GetProperty("userId").GetString();
        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");
        Assert.Equal(userId, me.GetProperty("userId").GetString());

        using var logout = await PostAsync(client, "/api/v1/auth/logout");
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/auth/me")).StatusCode);

        using var login = await PostAsync(client, "/api/v1/auth/login",
            new { email, password = "StrongPass!123" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var restored = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");
        Assert.Equal(userId, restored.GetProperty("userId").GetString());
    }

    [Fact]
    public async Task GuestCookieSurvivesAccountLoginAndLogout()
    {
        using var factory = OpenFactory();
        using var client = factory.CreateClient();
        using var guest = await PostAsync(client, "/api/v1/guest-sessions");
        Assert.Equal(HttpStatusCode.Created, guest.StatusCode);
        Assert.Contains(guest.Headers.GetValues("Set-Cookie"), value =>
            value.StartsWith("gomoku.guest=") && value.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        var created = await guest.Content.ReadFromJsonAsync<JsonElement>();
        var guestId = created.GetProperty("guestSessionId").GetString();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/auth/me")).StatusCode);

        using var register = await PostAsync(client, "/api/v1/auth/register",
            new { email = $"task3-{Guid.NewGuid():N}@example.test", password = "StrongPass!123" });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        using var guestAgain = await PostAsync(client, "/api/v1/guest-sessions");
        Assert.Equal(HttpStatusCode.OK, guestAgain.StatusCode);
        Assert.Equal(guestId,
            (await guestAgain.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("guestSessionId").GetString());

        using var logout = await PostAsync(client, "/api/v1/auth/logout");
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var afterLogout = await PostAsync(client, "/api/v1/guest-sessions");
        Assert.Equal(guestId,
            (await afterLogout.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("guestSessionId").GetString());
    }

    [Fact]
    public async Task UnsafeRequestWithoutCsrfTokenIsRejected()
    {
        using var factory = OpenFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/auth/register",
            new { email = $"task3-{Guid.NewGuid():N}@example.test", password = "StrongPass!123" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AuthCookieWorksAfterServerRestartWithSameKeyRing()
    {
        var email = $"task3-{Guid.NewGuid():N}@example.test";
        string cookie;
        string userId;
        using (var firstFactory = OpenFactory())
        using (var firstClient = firstFactory.CreateClient())
        using (var register = await PostAsync(firstClient, "/api/v1/auth/register",
            new { email, password = "StrongPass!123" }))
        {
            Assert.Equal(HttpStatusCode.Created, register.StatusCode);
            cookie = register.Headers.GetValues("Set-Cookie")
                .Single(value => value.StartsWith("gomoku.auth="));
            userId = (await register.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("userId").GetString()!;
        }

        using var secondFactory = OpenFactory();
        using var secondClient = secondFactory.CreateClient();
        secondClient.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);
        var me = await secondClient.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");
        Assert.Equal(userId, me.GetProperty("userId").GetString());
    }

    [Fact]
    public async Task LosingGuestCookieCreatesNewSessionWithoutDeletingOldRow()
    {
        using var factory = OpenFactory();
        using var firstClient = factory.CreateClient();
        using var first = await PostAsync(firstClient, "/api/v1/guest-sessions");
        var firstId = Guid.Parse((await first.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("guestSessionId").GetString()!);

        using var secondClient = factory.CreateClient();
        using var second = await PostAsync(secondClient, "/api/v1/guest-sessions");
        var secondId = Guid.Parse((await second.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("guestSessionId").GetString()!);
        Assert.NotEqual(firstId, secondId);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GomokuDbContext>();
        Assert.True(await db.GuestSessions.AnyAsync(x => x.Id == firstId));
        Assert.True(await db.GuestSessions.AnyAsync(x => x.Id == secondId));
    }

    [Fact]
    public async Task InvalidLoginReturnsStableErrorShape()
    {
        using var factory = OpenFactory();
        using var client = factory.CreateClient();
        using var response = await PostAsync(client, "/api/v1/auth/login",
            new { email = "nobody@example.test", password = "WrongPass!123" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_credentials", error.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("message").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task DatabaseFailureReturnsStorageUnavailable()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Gomoku",
                "Host=127.0.0.1;Port=1;Database=gomoku;Username=missing;Timeout=1"));
        using var client = factory.CreateClient();
        using var response = await PostAsync(client, "/api/v1/guest-sessions");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("storage_unavailable", error.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("message").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task WeakPasswordReturnsValidationError()
    {
        using var factory = OpenFactory();
        using var client = factory.CreateClient();
        using var response = await PostAsync(client, "/api/v1/auth/register",
            new { email = $"task3-{Guid.NewGuid():N}@example.test", password = "weak" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("validation_error", error.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task ProductionCookiesAreSecureAndHttpOnly()
    {
        var connectionString = Environment.GetEnvironmentVariable("GOMOKU_TEST_POSTGRES")!;
        var keyPath = Environment.GetEnvironmentVariable("GOMOKU_DP_KEYS_DIR")!;
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:Gomoku", connectionString);
            builder.UseSetting("GOMOKU_DP_KEYS_DIR", keyPath);
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        using var guest = await PostAsync(client, "/api/v1/guest-sessions");
        Assert.Equal(HttpStatusCode.Created, guest.StatusCode);
        Assert.Contains(guest.Headers.GetValues("Set-Cookie"), value =>
            value.StartsWith("gomoku.guest=") && value.Contains("secure", StringComparison.OrdinalIgnoreCase)
            && value.Contains("httponly", StringComparison.OrdinalIgnoreCase));

        using var register = await PostAsync(client, "/api/v1/auth/register",
            new { email = $"task3-{Guid.NewGuid():N}@example.test", password = "StrongPass!123" });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        Assert.Contains(register.Headers.GetValues("Set-Cookie"), value =>
            value.StartsWith("gomoku.auth=") && value.Contains("secure", StringComparison.OrdinalIgnoreCase)
            && value.Contains("httponly", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TamperedCsrfTokenCannotCreateGuestSession()
    {
        using var factory = OpenFactory();
        using var client = factory.CreateClient();
        await client.GetAsync("/api/v1/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/guest-sessions");
        request.Headers.Add("X-CSRF-TOKEN", "tampered-token");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(response.Headers, header => header.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase)
            && header.Value.Any(value => value.StartsWith("gomoku.guest=")));
    }
}
