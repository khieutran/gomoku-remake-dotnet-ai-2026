using Gomoku.Core;

namespace Gomoku.AI;

internal static class LegalFallback
{
    public static Position? Choose(IReadOnlyBoard board, int seed, CancellationToken cancellationToken)
    {
        var candidates = LegalMoveCandidates.NearOccupied(board, cancellationToken);
        if (candidates.Count > 0)
            return candidates[new Random(seed).Next(candidates.Count)];

        var center = new Position(board.Size / 2, board.Size / 2);
        return board.Get(center) == Stone.Empty ? center : null;
    }
}
