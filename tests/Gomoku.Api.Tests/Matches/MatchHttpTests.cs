using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Gomoku.Api.Tests.Matches;

public sealed class MatchHttpTests
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
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("requestToken").GetString());
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task GuestCreateGetAndRetryPreserveOriginalResponse()
    {
        using var factory = OpenFactory();
        using var client = factory.CreateClient();
        using var guest = await PostAsync(client, "/api/v1/guest-sessions");
        Assert.Equal(HttpStatusCode.Created, guest.StatusCode);

        var commandId = "create-" + Guid.NewGuid();
        using var first = await PostAsync(client, "/api/v1/matches?scope=guest", new { commandId });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstBody = await first.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(firstBody);
        var matchId = json.RootElement.GetProperty("matchId").GetGuid();
        Assert.Equal(20, json.RootElement.GetProperty("boardSize").GetInt32());
        Assert.Equal(0, json.RootElement.GetProperty("revision").GetInt64());
        Assert.Equal("X", json.RootElement.GetProperty("currentGame").GetProperty("nextStone").GetString());

        using var read = await client.GetAsync($"/api/v1/matches/{matchId}?scope=guest");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(firstBody, await read.Content.ReadAsStringAsync());

        using var retry = await PostAsync(client, "/api/v1/matches?scope=guest", new { commandId });
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.Equal(firstBody, await retry.Content.ReadAsStringAsync());

        using var otherCommand = await PostAsync(client, "/api/v1/matches?scope=guest",
            new { commandId = "other-" + Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Created, otherCommand.StatusCode);
        using var retryAgain = await PostAsync(client, "/api/v1/matches?scope=guest", new { commandId });
        Assert.Equal(firstBody, await retryAgain.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task OtherGuestCannotReadMatchAndAccountDoesNotInheritIt()
    {
        using var factory = OpenFactory();
        using var firstClient = factory.CreateClient();
        using var firstGuest = await PostAsync(firstClient, "/api/v1/guest-sessions");
        using var created = await PostAsync(firstClient, "/api/v1/matches?scope=guest",
            new { commandId = "create-" + Guid.NewGuid() });
        var matchId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("matchId").GetGuid();

        using var secondClient = factory.CreateClient();
        using var secondGuest = await PostAsync(secondClient, "/api/v1/guest-sessions");
        using var otherRead = await secondClient.GetAsync($"/api/v1/matches/{matchId}?scope=guest");
        Assert.Equal(HttpStatusCode.NotFound, otherRead.StatusCode);
        Assert.Equal("not_found", (await otherRead.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString());

        using var register = await PostAsync(firstClient, "/api/v1/auth/register",
            new { email = $"task5-{Guid.NewGuid():N}@example.test", password = "StrongPass!123" });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        using var accountRead = await firstClient.GetAsync($"/api/v1/matches/{matchId}");
        Assert.Equal(HttpStatusCode.NotFound, accountRead.StatusCode);
        using var guestRead = await firstClient.GetAsync($"/api/v1/matches/{matchId}?scope=guest");
        Assert.Equal(HttpStatusCode.OK, guestRead.StatusCode);

        using var accountMatch = await PostAsync(firstClient, "/api/v1/matches",
            new { commandId = "account-" + Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Created, accountMatch.StatusCode);
        var accountId = (await accountMatch.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("matchId").GetGuid();
        using var wrongScope = await firstClient.GetAsync($"/api/v1/matches/{accountId}?scope=guest");
        Assert.Equal(HttpStatusCode.NotFound, wrongScope.StatusCode);
    }

    [Fact]
    public async Task MissingIdentityAndInvalidInputReturnStableErrors()
    {
        using var factory = OpenFactory();
        using var client = factory.CreateClient();

        using var noGuest = await PostAsync(client, "/api/v1/matches?scope=guest",
            new { commandId = "create-" + Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Unauthorized, noGuest.StatusCode);
        Assert.Equal("authentication_required", (await noGuest.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString());

        using var noAccount = await PostAsync(client, "/api/v1/matches?scope=account",
            new { commandId = "create-" + Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Unauthorized, noAccount.StatusCode);

        using var guest = await PostAsync(client, "/api/v1/guest-sessions");
        using var missingCommand = await PostAsync(client, "/api/v1/matches?scope=guest", new { });
        Assert.Equal(HttpStatusCode.BadRequest, missingCommand.StatusCode);
        var error = await missingCommand.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("validation_error", error.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("traceId").GetString()));

        using var invalidScope = await PostAsync(client, "/api/v1/matches?scope=other",
            new { commandId = "create-" + Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.BadRequest, invalidScope.StatusCode);
        using var invalidId = await client.GetAsync("/api/v1/matches/not-a-guid?scope=guest");
        Assert.Equal(HttpStatusCode.BadRequest, invalidId.StatusCode);

        using var wrongType = await PostAsync(client, "/api/v1/matches?scope=guest",
            new { commandId = 123 });
        Assert.Equal(HttpStatusCode.BadRequest, wrongType.StatusCode);
        using var tooLong = await PostAsync(client, "/api/v1/matches?scope=guest",
            new { commandId = new string('x', 129) });
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);

        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        using var malformed = new HttpRequestMessage(HttpMethod.Post, "/api/v1/matches?scope=guest");
        malformed.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("requestToken").GetString());
        malformed.Content = new StringContent("{", Encoding.UTF8, "application/json");
        using var malformedResponse = await client.SendAsync(malformed);
        Assert.Equal(HttpStatusCode.BadRequest, malformedResponse.StatusCode);
        Assert.Equal("validation_error", (await malformedResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString());
    }

    [Fact]
    public async Task MatchCreationRequiresCsrfAndDoesNotExposeMoveRoutes()
    {
        using var factory = OpenFactory();
        using var client = factory.CreateClient();
        using var guest = await PostAsync(client, "/api/v1/guest-sessions");
        using var withoutCsrf = await client.PostAsJsonAsync("/api/v1/matches?scope=guest",
            new { commandId = "create-" + Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.BadRequest, withoutCsrf.StatusCode);
        Assert.Equal("csrf_invalid", (await withoutCsrf.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString());

        using var noMoveRoute = await PostAsync(client, $"/api/v1/matches/{Guid.NewGuid()}/moves?scope=guest",
            new { row = 0, column = 0, expectedRevision = 0, commandId = Guid.NewGuid().ToString() });
        Assert.Equal(HttpStatusCode.NotFound, noMoveRoute.StatusCode);
    }

    [Fact]
    public async Task AccountMatchRemainsAvailableAfterLosingCookieAndSigningInAgain()
    {
        using var factory = OpenFactory();
        using var firstClient = factory.CreateClient();
        var email = $"task5-{Guid.NewGuid():N}@example.test";
        using var register = await PostAsync(firstClient, "/api/v1/auth/register",
            new { email, password = "StrongPass!123" });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        using var created = await PostAsync(firstClient, "/api/v1/matches",
            new { commandId = "account-" + Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var matchId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("matchId").GetGuid();

        using var secondClient = factory.CreateClient();
        using var noCookie = await secondClient.GetAsync($"/api/v1/matches/{matchId}?scope=account");
        Assert.Equal(HttpStatusCode.Unauthorized, noCookie.StatusCode);
        using var login = await PostAsync(secondClient, "/api/v1/auth/login",
            new { email, password = "StrongPass!123" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var restored = await secondClient.GetAsync($"/api/v1/matches/{matchId}?scope=account");
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        Assert.Equal(matchId, (await restored.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("matchId").GetGuid());
    }

    [Fact]
    public async Task ActiveMatchListLetsAccountResumeAfterLoginWithoutLeakingGuestMatch()
    {
        using var factory = OpenFactory();
        using var first = factory.CreateClient();
        var email = $"resume-{Guid.NewGuid():N}@example.test";
        using var register = await PostAsync(first, "/api/v1/auth/register",
            new { email, password = "StrongPass!123" });
        using var accountMatch = await PostAsync(first, "/api/v1/matches?scope=account",
            new { commandId = "account-resume-" + Guid.NewGuid() });
        var accountId = (await accountMatch.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("matchId").GetGuid();
        using var guest = await PostAsync(first, "/api/v1/guest-sessions");
        using var guestMatch = await PostAsync(first, "/api/v1/matches?scope=guest",
            new { commandId = "guest-resume-" + Guid.NewGuid() });
        var guestId = (await guestMatch.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("matchId").GetGuid();

        using var second = factory.CreateClient();
        using var noCookie = await second.GetAsync("/api/v1/matches/active?scope=account");
        Assert.Equal(HttpStatusCode.Unauthorized, noCookie.StatusCode);
        using var login = await PostAsync(second, "/api/v1/auth/login",
            new { email, password = "StrongPass!123" });
        using var accountList = await second.GetAsync("/api/v1/matches/active?scope=account");
        Assert.Equal(HttpStatusCode.OK, accountList.StatusCode);
        var accountItems = (await accountList.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items").EnumerateArray().ToArray();
        Assert.Contains(accountItems, x => x.GetProperty("matchId").GetGuid() == accountId);
        Assert.DoesNotContain(accountItems, x => x.GetProperty("matchId").GetGuid() == guestId);
        using var noGuestCookie = await second.GetAsync("/api/v1/matches/active?scope=guest");
        Assert.Equal(HttpStatusCode.Unauthorized, noGuestCookie.StatusCode);

        using var guestList = await first.GetAsync("/api/v1/matches/active?scope=guest");
        Assert.Equal(HttpStatusCode.OK, guestList.StatusCode);
        var guestItems = (await guestList.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items").EnumerateArray().ToArray();
        Assert.Contains(guestItems, x => x.GetProperty("matchId").GetGuid() == guestId);
        Assert.DoesNotContain(guestItems, x => x.GetProperty("matchId").GetGuid() == accountId);
    }

    [Fact]
    public async Task CreateRejectsFieldsItDoesNotUnderstand()
    {
        using var factory = OpenFactory();
        using var client = factory.CreateClient();
        using var guest = await PostAsync(client, "/api/v1/guest-sessions");
        var commandId = "create-" + Guid.NewGuid();

        using var first = await PostAsync(client, "/api/v1/matches?scope=guest", new { commandId });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var changed = await PostAsync(client, "/api/v1/matches?scope=guest",
            new { commandId, boardSize = 15 });
        Assert.Equal(HttpStatusCode.BadRequest, changed.StatusCode);
        Assert.Equal("validation_error", (await changed.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString());
    }
}
