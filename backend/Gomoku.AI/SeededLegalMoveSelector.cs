using Gomoku.Core;

namespace Gomoku.AI;

public sealed class SeededLegalMoveSelector(int seed) : IAiMoveSelector
{
    public Position? SelectMove(GameSnapshot game, Stone side, TimeSpan budget,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        cancellationToken.ThrowIfCancellationRequested();
        if (budget < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(budget));
        if (side is not (Stone.X or Stone.O))
            throw new ArgumentOutOfRangeException(nameof(side));
        if (game.Status != GameStatus.InProgress)
            return null;
        if (game.NextStone != side)
            throw new ArgumentException("The requested side is not next to play.", nameof(side));

        var board = game.Board;
        if (game.MoveIndex == 0)
            return new Position(board.Size / 2, board.Size / 2);

        var candidates = LegalMoveCandidates.NearOccupied(board, cancellationToken);
        return candidates.Count == 0 ? null : candidates[new Random(seed).Next(candidates.Count)];
    }
}
