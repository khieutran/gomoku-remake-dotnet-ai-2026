using Gomoku.Core;

namespace Gomoku.AI;

internal static class LegalMoveCandidates
{
    public static List<Position> NearOccupied(IReadOnlyBoard board, CancellationToken cancellationToken)
    {
        var candidates = new HashSet<Position>();
        for (var row = 0; row < board.Size; row++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var column = 0; column < board.Columns; column++)
            {
                if (board.Get(new Position(row, column)) == Stone.Empty)
                    continue;
                for (var neighborRow = Math.Max(0, row - 1);
                     neighborRow <= Math.Min(board.Size - 1, row + 1); neighborRow++)
                for (var neighborColumn = Math.Max(0, column - 1);
                     neighborColumn <= Math.Min(board.Columns - 1, column + 1); neighborColumn++)
                {
                    var neighbor = new Position(neighborRow, neighborColumn);
                    if (board.Get(neighbor) == Stone.Empty)
                        candidates.Add(neighbor);
                }
            }
        }
        return candidates.OrderBy(position => position.Row)
            .ThenBy(position => position.Column).ToList();
    }
}
