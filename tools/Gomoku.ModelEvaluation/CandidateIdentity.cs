using System.Text.Json;

namespace Gomoku.ModelEvaluation;

public static class CandidateIdentity
{
    public static string FromSource(JsonElement source, string configSha256,
        string trainerSha256)
    {
        var material = source.GetProperty("datasetSha256").GetString() +
            configSha256 + trainerSha256;
        if (source.TryGetProperty("selfPlay", out var selfPlay))
            material += selfPlay.GetProperty("datasetSha256").GetString() +
                selfPlay.GetProperty("manifestSha256").GetString();
        return "policy-v1-" + ReportStore.Sha256Text(material)[..16];
    }
}
