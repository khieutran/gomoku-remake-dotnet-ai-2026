using System.Collections.Concurrent;
using System.Security.Cryptography;
using Gomoku.AI;
using Gomoku.Core;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Gomoku.Infrastructure.AI;

public sealed record PublishedPolicyModel(string Path, string Sha256);

/// <summary>Only configured versions are eligible. An artifact never activates itself.</summary>
public sealed class OnnxPolicyModelRegistry : IPolicyModelRegistry, IDisposable
{
    private readonly IReadOnlyDictionary<string, PublishedPolicyModel> _publishedModels;
    private readonly bool _verifyHash;
    private readonly ConcurrentDictionary<string, OnnxPolicyModel> _models = new();
    private int _disposed;

    /// <summary>For live versions: each pin must include the expected weight hash.</summary>
    public OnnxPolicyModelRegistry(IReadOnlyDictionary<string, PublishedPolicyModel> publishedModels)
    {
        _publishedModels = publishedModels.ToDictionary(item => item.Key,
            item => item.Value, StringComparer.Ordinal);
        _verifyHash = true;
    }

    /// <summary>For offline candidate evaluation before a version is published.</summary>
    public OnnxPolicyModelRegistry(IReadOnlyDictionary<string, string> candidatePaths)
    {
        _publishedModels = candidatePaths.ToDictionary(item => item.Key,
            item => new PublishedPolicyModel(item.Value, string.Empty), StringComparer.Ordinal);
        _verifyHash = false;
    }

    public IPolicyModel? Find(string aiVersion)
    {
        if (!_publishedModels.TryGetValue(aiVersion, out var artifact) ||
            string.IsNullOrWhiteSpace(artifact.Path)) return null;
        return _models.GetOrAdd(aiVersion, _ => new OnnxPolicyModel(artifact.Path,
            _verifyHash ? artifact.Sha256 : null));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        foreach (var model in _models.Values) model.Dispose();
    }

    private sealed class OnnxPolicyModel(string path, string? expectedSha256)
        : IPolicyModel, IDisposable
    {
        private readonly Lazy<InferenceSession> _session = new(() => Load(path, expectedSha256),
            LazyThreadSafetyMode.ExecutionAndPublication);

        public float[] Score(BoardSnapshot board, Stone side, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(board);
            if (board.Size != 20 || side is not (Stone.X or Stone.O))
                throw new PolicyModelException("model_incompatible_board");
            cancellationToken.ThrowIfCancellationRequested();

            var tensor = new DenseTensor<float>([1, 3, 20, 20]);
            var other = side == Stone.X ? Stone.O : Stone.X;
            for (var row = 0; row < 20; row++)
            for (var column = 0; column < 20; column++)
            {
                var stone = board.Get(new Position(row, column));
                tensor[0, stone == side ? 0 : stone == other ? 1 : 2, row, column] = 1f;
            }

            try
            {
                using var output = _session.Value.Run(
                    [NamedOnnxValue.CreateFromTensor("board", tensor)]);
                cancellationToken.ThrowIfCancellationRequested();
                var value = output.FirstOrDefault(item => item.Name == "policy_logits")
                    ?? throw new PolicyModelException("model_invalid_output");
                var logits = value.AsTensor<float>();
                var shape = logits.Dimensions.ToArray();
                if (shape.Length != 2 || shape[0] != 1 || shape[1] != 400)
                    throw new PolicyModelException("model_invalid_output");
                return logits.ToArray();
            }
            catch (PolicyModelException) { throw; }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                throw new PolicyModelException("model_inference_error", exception);
            }
        }

        private static InferenceSession Load(string modelPath, string? expectedSha256)
        {
            if (!File.Exists(modelPath)) throw new PolicyModelException("model_missing");
            if (expectedSha256 is not null &&
                (expectedSha256.Length != 64 || !expectedSha256.All(Uri.IsHexDigit)))
                throw new PolicyModelException("model_hash_missing");
            try
            {
                var bytes = File.ReadAllBytes(modelPath);
                if (expectedSha256 is not null &&
                    !Convert.ToHexString(SHA256.HashData(bytes)).Equals(expectedSha256,
                        StringComparison.OrdinalIgnoreCase))
                    throw new PolicyModelException("model_hash_mismatch");
                return new InferenceSession(bytes);
            }
            catch (PolicyModelException) { throw; }
            catch (Exception exception)
            {
                throw new PolicyModelException("model_load_error", exception);
            }
        }

        public void Dispose()
        {
            if (_session.IsValueCreated) _session.Value.Dispose();
        }
    }
}
