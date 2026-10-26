using Gomoku.Core;

namespace Gomoku.AI;

/// <summary>Resolves an immutable, explicitly configured policy artifact by its pinned AI version.</summary>
public interface IPolicyModelRegistry
{
    IPolicyModel? Find(string aiVersion);
}

public interface IPolicyModel
{
    /// <summary>Returns raw row-major logits for every cell, including occupied cells.</summary>
    float[] Score(BoardSnapshot board, Stone side, CancellationToken cancellationToken);
}

public sealed class PolicyModelException(string reason, Exception? innerException = null)
    : Exception(reason, innerException)
{
    public string Reason { get; } = reason;
}
