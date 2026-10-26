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

        return LegalFallback.Choose(game.Board, seed, cancellationToken);
    }
}
