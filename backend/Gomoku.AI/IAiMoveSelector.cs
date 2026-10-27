using Gomoku.Core;

namespace Gomoku.AI;

public interface IAiMoveSelector
{
    Position? SelectMove(GameSnapshot game, Stone side, TimeSpan budget,
        CancellationToken cancellationToken);
}
