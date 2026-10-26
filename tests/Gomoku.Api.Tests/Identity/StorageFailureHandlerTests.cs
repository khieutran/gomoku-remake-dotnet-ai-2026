using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gomoku.Api.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Gomoku.Api.Tests.Identity;

public sealed class StorageFailureHandlerTests
{
    [Fact]
    public async Task NontransientUpdateFailureIsNotReportedAsStorageOutage()
    {
        using var server = new TestServer(new WebHostBuilder().Configure(app =>
        {
            app.UseStorageFailureHandler();
            app.Run(_ => throw new DbUpdateException("constraint failure"));
        }));

        await Assert.ThrowsAsync<DbUpdateException>(() => server.CreateClient().GetAsync("/"));
    }

    [Fact]
    public async Task TimeoutReturnsStableStorageUnavailableResponse()
    {
        using var server = new TestServer(new WebHostBuilder().Configure(app =>
        {
            app.UseStorageFailureHandler();
            app.Run(_ => throw new TimeoutException("database timed out"));
        }));

        using var response = await server.CreateClient().GetAsync("/");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("storage_unavailable", error.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(error.GetProperty("traceId").GetString()));
    }
}
