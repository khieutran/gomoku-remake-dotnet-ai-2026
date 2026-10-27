using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Gomoku.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace Gomoku.Api.Tests.Identity;

public sealed class IdentityHttpTests
{
    private sealed class RegistrationBarrierValidator : IUserValidator<ApplicationUser>
    {
        private readonly TaskCompletionSource _bothValidated = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _validated;

        public async Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager,
            ApplicationUser user)
        {
            if (Interlocked.Increment(ref _validated) == 2)
            {
                _bothValidated.TrySetResult();
            }

            await _bothValidated.Task.WaitAsync(TimeSpan.FromSeconds(15));
            return IdentityResult.Success;
        }
    }

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
    public async Task ConcurrentRegistrationOfSameNormalizedEmailCreatesOneAccount()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var email = $"task6-{Guid.NewGuid():N}@example.test";
            using var factory = OpenFactory().WithWebHostBuilder(builder =>
                builder.ConfigureTestServices(services =>
                    services.AddSingleton<IUserValidator<ApplicationUser>>(new RegistrationBarrierValidator())));
            using var firstClient = factory.CreateClient();
            using var secondClient = factory.CreateClient();

            var first = PostAsync(firstClient, "/api/v1/auth/register",
                new { email, password = "StrongPass!123" });
            var second = PostAsync(secondClient, "/api/v1/auth/register",
                new { email = email.ToUpperInvariant(), password = "StrongPass!123" });
            using var firstResponse = await first;
            using var secondResponse = await second;

            Assert.Equal(new[] { HttpStatusCode.Created, HttpStatusCode.Conflict },
                new[] { firstResponse.StatusCode, secondResponse.StatusCode }.Order());
            var conflict = firstResponse.StatusCode == HttpStatusCode.Conflict ? firstResponse : secondResponse;
            var error = await conflict.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("account_exists", error.GetProperty("code").GetString());

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<GomokuDbContext>();
            Assert.Equal(1, await db.Users.CountAsync(user => user.NormalizedUserName == email.ToUpperInvariant()));
        }
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
    public async Task CurrentGuestReturnsExistingSessionWithoutIssuingNewCookie()
    {
        using var factory = OpenFactory();
        using var client = factory.CreateClient();
        using var created = await PostAsync(client, "/api/v1/guest-sessions");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var guestId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("guestSessionId").GetString();

        using var current = await client.GetAsync("/api/v1/guest-sessions/current");
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        Assert.Equal(guestId, (await current.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("guestSessionId").GetString());
        Assert.False(current.Headers.Contains("Set-Cookie"));
        Assert.Contains("no-store", current.Headers.GetValues("Cache-Control").Single());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid-token")]
    public async Task CurrentGuestRejectsMissingOrInvalidCookie(string? token)
    {
        using var factory = OpenFactory();
        using var client = factory.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Add("Cookie", $"gomoku.guest={token}");
        }

        using var current = await client.GetAsync("/api/v1/guest-sessions/current");
        Assert.Equal(HttpStatusCode.Unauthorized, current.StatusCode);
        var error = await current.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("guest_session_required", error.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("traceId").GetString()));
        Assert.False(current.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task CurrentGuestRejectsExpiredCookie()
    {
        using var factory = OpenFactory();
        using var client = factory.CreateClient();
        using var created = await PostAsync(client, "/api/v1/guest-sessions");
        var guestId = Guid.Parse((await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("guestSessionId").GetString()!);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GomokuDbContext>();
            var session = await db.GuestSessions.SingleAsync(x => x.Id == guestId);
            session.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        using var current = await client.GetAsync("/api/v1/guest-sessions/current");
        Assert.Equal(HttpStatusCode.Unauthorized, current.StatusCode);
        Assert.Equal("guest_session_required", (await current.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString());
        Assert.False(current.Headers.Contains("Set-Cookie"));
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

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public async Task InvalidRegistrationEmailReturnsValidationError(string email)
    {
        using var factory = OpenFactory();
        using var client = factory.CreateClient();
        using var response = await PostAsync(client, "/api/v1/auth/register",
            new { email, password = "StrongPass!123" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("validation_error", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task RegistrationDatabaseFailureReturnsStorageUnavailable()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Gomoku",
                "Host=127.0.0.1;Port=1;Database=gomoku;Username=missing;Timeout=1"));
        using var client = factory.CreateClient();
        using var response = await PostAsync(client, "/api/v1/auth/register",
            new { email = $"task6-{Guid.NewGuid():N}@example.test", password = "StrongPass!123" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("storage_unavailable", error.GetProperty("code").GetString());
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
