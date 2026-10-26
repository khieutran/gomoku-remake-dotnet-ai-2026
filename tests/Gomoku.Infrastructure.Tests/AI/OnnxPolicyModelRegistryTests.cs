using Gomoku.Infrastructure.AI;
using Gomoku.Core;

namespace Gomoku.Infrastructure.Tests.AI;

public sealed class OnnxPolicyModelRegistryTests
{
    [Fact]
    public void MissingPublishedArtifactReportsLoadFallbackReason()
    {
        var path = Path.Combine(Path.GetTempPath(),
            "missing-policy-" + Guid.NewGuid().ToString("N") + ".onnx");
        using var registry = new OnnxPolicyModelRegistry(
            new Dictionary<string, string> { ["search-v1+policy-pinned"] = path });
        var model = registry.Find("search-v1+policy-pinned");
        Assert.NotNull(model);
        Assert.Null(registry.Find("search-v1+policy-unpublished"));
        var board = new Board(20).Snapshot();

        var exception = Assert.Throws<Gomoku.AI.PolicyModelException>(() =>
            model.Score(board, Stone.O, CancellationToken.None));

        Assert.Equal("model_missing", exception.Reason);
    }

    [Fact]
    public void PublishedVersionRejectsChangedWeightsBeforeInference()
    {
        var path = Path.Combine(Path.GetTempPath(),
            "policy-" + Guid.NewGuid().ToString("N") + ".onnx");
        try
        {
            File.WriteAllBytes(path, [1, 2, 3]);
            using var registry = new OnnxPolicyModelRegistry(
                new Dictionary<string, PublishedPolicyModel>
                {
                    ["search-v1+policy-pinned"] = new(path, new string('0', 64))
                });
            var model = registry.Find("search-v1+policy-pinned");
            Assert.NotNull(model);

            var exception = Assert.Throws<Gomoku.AI.PolicyModelException>(() =>
                model.Score(new Board(20).Snapshot(), Stone.O, CancellationToken.None));

            Assert.Equal("model_hash_mismatch", exception.Reason);
        }
        finally { File.Delete(path); }
    }
}
