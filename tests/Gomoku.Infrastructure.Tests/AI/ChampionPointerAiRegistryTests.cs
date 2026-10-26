using System.Text.Json;
using Gomoku.Infrastructure.AI;

namespace Gomoku.Infrastructure.Tests.AI;

public sealed class ChampionPointerAiRegistryTests
{
    [Fact]
    public void MissingPointerKeepsSearchDefault()
    {
        using var registry = new ChampionPointerAiRegistry(null);

        Assert.Equal("search-v1", registry.CurrentVersion);
        Assert.Null(registry.Find("policy-v1-old"));
    }

    [Fact]
    public void RollbackChangesNewGameVersionButKeepsOldPinResolvable()
    {
        var pointerPath = Path.Combine(Path.GetTempPath(),
            "champion-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var modelPath = Path.Combine(Path.GetTempPath(), "pinned-model.onnx");
            var model = Revision("policy-v1-approved", modelPath,
                new string('A', 64));
            File.WriteAllText(pointerPath, JsonSerializer.Serialize(new
            {
                schemaVersion = 1, current = model, history = Array.Empty<object>()
            }));
            using var registry = new ChampionPointerAiRegistry(pointerPath);
            Assert.Equal("policy-v1-approved", registry.CurrentVersion);
            Assert.NotNull(registry.Find("policy-v1-approved"));

            File.WriteAllText(pointerPath, JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                current = Revision("search-v1", null, null),
                history = new[] { model }
            }));

            Assert.Equal("search-v1", registry.CurrentVersion);
            Assert.NotNull(registry.Find("policy-v1-approved"));
        }
        finally { File.Delete(pointerPath); }
    }

    private static object Revision(string version, string? path, string? sha) => new
    {
        aiVersion = version, modelPath = path, modelSha256 = sha,
        reportSha256 = (string?)null, action = "test", reason = "test",
        atUtc = DateTimeOffset.UtcNow
    };
}
