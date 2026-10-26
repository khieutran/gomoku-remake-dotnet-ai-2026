using Gomoku.Core;

namespace Gomoku.AI;

public interface IAiMoveAnalyzer : IAiMoveSelector
{
    SearchResult Analyze(GameSnapshot game, Stone side, TimeSpan budget,
        CancellationToken cancellationToken);
}
