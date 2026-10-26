using System.Text.Json;

namespace Gomoku.ModelEvaluation.Tests;

public sealed class CandidateIdentityTests
{
    [Fact]
    public void SelfPlayHashChangesCandidateIdButGuestOnlyRemainsCompatible()
    {
        const string guest = "guest-hash";
        const string config = "config-hash";
        const string trainer = "trainer-hash";
        using var guestOnly = JsonDocument.Parse("{\"datasetSha256\":\"guest-hash\"}");
        using var combined = JsonDocument.Parse("""
            {"datasetSha256":"guest-hash","selfPlay":
             {"datasetSha256":"self-play-hash","manifestSha256":"admission-hash"}}
            """);

        var original = "policy-v1-" + ReportStore.Sha256Text(guest + config + trainer)[..16];
        Assert.Equal(original, CandidateIdentity.FromSource(guestOnly.RootElement, config, trainer));
        Assert.NotEqual(original, CandidateIdentity.FromSource(combined.RootElement, config, trainer));
    }
}
