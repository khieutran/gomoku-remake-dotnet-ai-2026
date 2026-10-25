using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Gomoku.Api.Tests.Health;

public sealed class ReadinessHttpTests
{
    [Fact]
    public async Task PostgreSqlOutageMakesReadyUnhealthyWhileHealthRemainsLive()
    {
        using var factory = OpenFactory(
            "Host=127.0.0.1;Port=1;Database=gomoku;Username=gomoku;Password=unused;Timeout=1",
            "127.0.0.1:1,abortConnect=false,connectTimeout=100,asyncTimeout=100");
        using var client = factory.CreateClient();

        using var live = await client.GetAsync("/health");
        using var ready = await client.GetAsync("/ready");

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
    }

    [Fact]
    public async Task RedisOutageKeepsReadyHealthyAndPostgreSqlDataAvailable()
    {
        var postgres = Environment.GetEnvironmentVariable("GOMOKU_TEST_POSTGRES")
            ?? throw new InvalidOperationException("Set GOMOKU_TEST_POSTGRES.");
        using var factory = OpenFactory(postgres,
            "127.0.0.1:1,abortConnect=false,connectTimeout=100,asyncTimeout=100");
        using var client = factory.CreateClient();

        using var ready = await client.GetAsync("/ready");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);

        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        using var guestRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/guest-sessions");
        guestRequest.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("requestToken").GetString());
        using var guest = await client.SendAsync(guestRequest);
        Assert.Equal(HttpStatusCode.Created, guest.StatusCode);

        using var matchRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/matches?scope=guest");
        matchRequest.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("requestToken").GetString());
        matchRequest.Content = JsonContent.Create(new { commandId = "readiness-" + Guid.NewGuid() });
        using var match = await client.SendAsync(matchRequest);
        Assert.Equal(HttpStatusCode.Created, match.StatusCode);
        var matchId = (await match.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("matchId").GetGuid();

        using var storedMatch = await client.GetAsync($"/api/v1/matches/{matchId}?scope=guest");
        Assert.Equal(HttpStatusCode.OK, storedMatch.StatusCode);
        Assert.Equal(matchId, (await storedMatch.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("matchId").GetGuid());
    }

    private static WebApplicationFactory<Program> OpenFactory(string postgres, string redis) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Gomoku", postgres);
            builder.UseSetting("ConnectionStrings:Redis", redis);
        });
}
