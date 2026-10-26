using System.Collections.Concurrent;
using System.Text.Json;
using Gomoku.AI;
using Gomoku.Application.Matches;

namespace Gomoku.Infrastructure.AI;

/// <summary>Reads Task 7's atomic champion pointer; exact historical pins remain resolvable.</summary>
public sealed class ChampionPointerAiRegistry(string? pointerPath)
    : IAiVersionProvider, IPolicyModelRegistry, IDisposable
{
    private readonly ConcurrentDictionary<string, CachedModel> _models = new(StringComparer.Ordinal);
    private int _disposed;

    public string CurrentVersion
    {
        get
        {
            using var document = ReadPointer();
            if (document is null || !TryCurrent(document.RootElement, out var current))
                return AiVersions.Current;
            if (current.Version == AiVersions.SearchV1 ||
                current.Version == AiVersions.LegalV1)
                return current.Version;
            return IsModelVersion(current.Version) && ValidArtifact(current)
                ? current.Version : AiVersions.Current;
        }
    }

    public IPolicyModel? Find(string aiVersion)
    {
        if (!IsModelVersion(aiVersion)) return null;
        using var document = ReadPointer();
        if (document is null || !TryFind(document.RootElement, aiVersion, out var artifact))
            return null;
        if (!ValidArtifact(artifact)) return null;

        var cached = _models.GetOrAdd(aiVersion, _ =>
        {
            var registry = new OnnxPolicyModelRegistry(
                new Dictionary<string, PublishedPolicyModel>(StringComparer.Ordinal)
                {
                    [aiVersion] = new(artifact.Path!, artifact.Sha256!)
                });
            return new CachedModel(artifact.Path!, artifact.Sha256!, registry);
        });
        // A version can never be repointed to different weights, even if the pointer
        // is edited while this process is running.
        if (cached.Path != artifact.Path ||
            !cached.Sha256.Equals(artifact.Sha256, StringComparison.OrdinalIgnoreCase))
            return null;
        return cached.Registry.Find(aiVersion);
    }

    private JsonDocument? ReadPointer()
    {
        if (string.IsNullOrWhiteSpace(pointerPath)) return null;
        try
        {
            using var stream = File.OpenRead(pointerPath);
            var document = JsonDocument.Parse(stream);
            if (!document.RootElement.TryGetProperty("schemaVersion", out var schema) ||
                schema.GetInt32() != 1)
            {
                document.Dispose();
                return null;
            }
            return document;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or JsonException or InvalidOperationException or FormatException
            or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static bool TryCurrent(JsonElement root, out ModelArtifact artifact)
    {
        artifact = default;
        return root.TryGetProperty("current", out var current) && Parse(current, out artifact);
    }

    private static bool TryFind(JsonElement root, string version, out ModelArtifact artifact)
    {
        artifact = default;
        var found = false;
        if (TryCurrent(root, out var current) && current.Version == version)
        {
            artifact = current;
            found = true;
        }
        if (!root.TryGetProperty("history", out var history) ||
            history.ValueKind != JsonValueKind.Array) return found;
        foreach (var entry in history.EnumerateArray())
        {
            if (!Parse(entry, out var previous) || previous.Version != version) continue;
            if (found && (artifact.Path != previous.Path ||
                !string.Equals(artifact.Sha256, previous.Sha256,
                    StringComparison.OrdinalIgnoreCase)))
                return false;
            artifact = previous;
            found = true;
        }
        return found;
    }

    private static bool Parse(JsonElement element, out ModelArtifact artifact)
    {
        artifact = default;
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("aiVersion", out var version) ||
            version.ValueKind != JsonValueKind.String) return false;
        var name = version.GetString();
        if (string.IsNullOrWhiteSpace(name)) return false;
        var path = element.TryGetProperty("modelPath", out var pathValue) &&
            pathValue.ValueKind == JsonValueKind.String ? pathValue.GetString() : null;
        var sha = element.TryGetProperty("modelSha256", out var shaValue) &&
            shaValue.ValueKind == JsonValueKind.String ? shaValue.GetString() : null;
        artifact = new ModelArtifact(name, path, sha);
        return true;
    }

    private static bool IsModelVersion(string? version) =>
        version?.StartsWith("policy-v1-", StringComparison.Ordinal) == true ||
        version?.StartsWith("search-v1+policy-", StringComparison.Ordinal) == true;

    private static bool ValidArtifact(ModelArtifact artifact) =>
        !string.IsNullOrWhiteSpace(artifact.Path) && Path.IsPathFullyQualified(artifact.Path) &&
        artifact.Sha256 is { Length: 64 } sha && sha.All(Uri.IsHexDigit);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        foreach (var model in _models.Values) model.Registry.Dispose();
    }

    private readonly record struct ModelArtifact(string Version, string? Path, string? Sha256);
    private sealed record CachedModel(string Path, string Sha256, OnnxPolicyModelRegistry Registry);
}
