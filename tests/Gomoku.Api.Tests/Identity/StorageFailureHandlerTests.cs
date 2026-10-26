using System.Text.Json;
using Gomoku.Api.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Gomoku.Api.Tests.Identity;

public sealed class StorageFailureHandlerTests
{
    [Fact]
    public async Task NontransientUpdateFailureIsNotReportedAsStorageOutage()
    {
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var app = new ApplicationBuilder(services);
        app.UseStorageFailureHandler();
        app.Run(_ => throw new DbUpdateException("constraint failure"));
        var context = new DefaultHttpContext { RequestServices = services };

        await Assert.ThrowsAsync<DbUpdateException>(() => app.Build()(context));
    }

    [Fact]
    public async Task TimeoutReturnsStableStorageUnavailableResponse()
    {
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var app = new ApplicationBuilder(services);
        app.UseStorageFailureHandler();
        app.Run(_ => throw new TimeoutException("database timed out"));
        var context = new DefaultHttpContext { RequestServices = services };
        context.Response.Body = new MemoryStream();

        await app.Build()(context);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var error = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal("storage_unavailable", error.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(error.RootElement.GetProperty("traceId").GetString()));
    }
}
