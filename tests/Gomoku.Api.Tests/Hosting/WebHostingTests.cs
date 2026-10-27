using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Gomoku.Api.Tests.Hosting;

public sealed class WebHostingTests
{
    private sealed class Site : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"gomoku-web-{Guid.NewGuid():N}");
        public WebApplicationFactory<Program> Factory { get; }

        public Site()
        {
            Directory.CreateDirectory(_root);
            Directory.CreateDirectory(Path.Combine(_root, "assets"));
            File.WriteAllText(Path.Combine(_root, "index.html"), "<!doctype html><title>Gomoku test shell</title>");
            File.WriteAllText(Path.Combine(_root, "assets", "app-a1b2c3d4.js"), "export const app = true;");
            File.WriteAllText(Path.Combine(_root, "assets", "app-D2AdDWe-.js"), "export const chunk = true;");
            File.WriteAllText(Path.Combine(_root, "assets", "app-a1b2c3d4.css"), "body { color: green; }");
            var keyRoot = Path.Combine(_root, "keys");
            Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseWebRoot(_root);
                builder.UseSetting("ConnectionStrings:Gomoku", "Host=127.0.0.1;Port=1;Database=gomoku;Username=missing;Timeout=1");
                builder.UseEnvironment("Production");
                builder.UseSetting("GOMOKU_DP_KEYS_DIR", keyRoot);
            });
        }

        public void Dispose()
        {
            Factory.Dispose();
            Directory.Delete(_root, recursive: true);
        }
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/play/123")]
    [InlineData("/history/123")]
    public async Task FrontendGetRoutesServeRevalidatedHtml(string route)
    {
        using var site = new Site();
        using var client = site.Factory.CreateClient();
        using var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Gomoku test shell", await response.Content.ReadAsStringAsync());
        Assert.Contains("no-cache", response.Headers.GetValues("Cache-Control").Single());
    }

    [Fact]
    public async Task FrontendHeadRouteReturnsHtmlHeadersWithoutBody()
    {
        using var site = new Site();
        using var client = site.Factory.CreateClient();
        using var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/history/123"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-cache", response.Headers.GetValues("Cache-Control").Single());
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Theory]
    [InlineData("/assets/app-a1b2c3d4.js", "text/javascript")]
    [InlineData("/assets/app-D2AdDWe-.js", "text/javascript")]
    [InlineData("/assets/app-a1b2c3d4.css", "text/css")]
    public async Task HashedAssetsHaveCorrectMimeAndImmutableCache(string route, string mime)
    {
        using var site = new Site();
        using var client = site.Factory.CreateClient();
        using var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(mime, response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("immutable", response.Headers.GetValues("Cache-Control").Single());
        Assert.Contains("max-age=31536000", response.Headers.GetValues("Cache-Control").Single());
    }

    [Theory]
    [InlineData("/api/v1/does-not-exist")]
    [InlineData("/api/v1/does-not-exist.json")]
    public async Task UnknownApiRouteReturnsJsonErrorWithoutSpaFallback(string route)
    {
        using var site = new Site();
        using var client = site.Factory.CreateClient();
        using var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("not_found", error.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("traceId").GetString()));
        Assert.Contains("private", response.Headers.GetValues("Cache-Control").Single());
        Assert.Contains("no-store", response.Headers.GetValues("Cache-Control").Single());
    }

    [Theory]
    [InlineData("/history/123", "POST")]
    [InlineData("/api/v1/missing", "GET")]
    [InlineData("/health/missing", "GET")]
    [InlineData("/assets/missing.js", "GET")]
    public async Task NonFrontendRequestsNeverReturnSpaHtml(string route, string method)
    {
        using var site = new Site();
        using var client = site.Factory.CreateClient();
        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), route));

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task BrowserSecurityHeadersArePresentOnHtmlAndApiResponses()
    {
        using var site = new Site();
        using var client = site.Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        using var html = await client.GetAsync("/");
        using var api = await client.GetAsync("/api/v1/auth/csrf");

        foreach (var response in new[] { html, api })
        {
            Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
            Assert.Equal("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").Single());
            var csp = response.Headers.GetValues("Content-Security-Policy").Single();
            Assert.Contains("default-src 'self'", csp);
            Assert.Contains("script-src 'self'", csp);
            Assert.Contains("object-src 'none'", csp);
        }
        Assert.Contains("private", api.Headers.GetValues("Cache-Control").Single());
        Assert.Contains("no-store", api.Headers.GetValues("Cache-Control").Single());
    }

    [Fact]
    public async Task ProductionCsrfCookieIsSecureAndMissingTokenCannotPost()
    {
        using var site = new Site();
        using var client = site.Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        using var csrf = await client.GetAsync("/api/v1/auth/csrf");
        Assert.Equal(HttpStatusCode.OK, csrf.StatusCode);
        Assert.Contains(csrf.Headers.GetValues("Set-Cookie"), cookie =>
            cookie.StartsWith("gomoku.csrf=") && cookie.Contains("secure", StringComparison.OrdinalIgnoreCase)
            && cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase));

        using var post = await client.PostAsJsonAsync("/api/v1/auth/register",
            new { email = "someone@example.test", password = "StrongPass!123" });
        Assert.Equal(HttpStatusCode.BadRequest, post.StatusCode);
        var error = await post.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("csrf_invalid", error.GetProperty("code").GetString());
        Assert.Contains("no-store", post.Headers.GetValues("Cache-Control").Single());
    }
}
