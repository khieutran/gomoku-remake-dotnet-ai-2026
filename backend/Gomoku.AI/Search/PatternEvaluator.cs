using Gomoku.Core;

namespace Gomoku.AI;

internal static class PatternEvaluator
{
    private static readonly (int Row, int Column)[] Directions =
    [(0, 1), (1, 0), (1, 1), (1, -1)];

    public static int Score(IReadOnlyBoard board, Stone side)
    {
        var total = 0;
        for (var row = 0; row < board.Size; row++)
        for (var column = 0; column < board.Columns; column++)
        {
            var start = new Position(row, column);
            if (board.Get(start) != side) continue;
            foreach (var (rowStep, columnStep) in Directions)
            {
                var before = new Position(row - rowStep, column - columnStep);
                if (Contains(board, before) && board.Get(before) == side) continue;

                var count = 1;
                var after = new Position(row + rowStep, column + columnStep);
                while (Contains(board, after) && board.Get(after) == side)
                {
                    count++;
                    after = new Position(after.Row + rowStep, after.Column + columnStep);
                }
                var openEnds = (Contains(board, before) && board.Get(before) == Stone.Empty ? 1 : 0) +
                    (Contains(board, after) && board.Get(after) == Stone.Empty ? 1 : 0);
                total += PatternScore(count, openEnds);
            }
        }
        return total;
    }

    public static int ScoreAt(IReadOnlyBoard board, Position move, Stone side)
    {
        var total = 0;
        foreach (var (rowStep, columnStep) in Directions)
        {
            var count = 1;
            var before = new Position(move.Row - rowStep, move.Column - columnStep);
            while (Contains(board, before) && board.Get(before) == side)
            {
                count++;
                before = new Position(before.Row - rowStep, before.Column - columnStep);
            }

            var after = new Position(move.Row + rowStep, move.Column + columnStep);
            while (Contains(board, after) && board.Get(after) == side)
            {
                count++;
                after = new Position(after.Row + rowStep, after.Column + columnStep);
            }

            var openEnds = (Contains(board, before) && board.Get(before) == Stone.Empty ? 1 : 0) +
                (Contains(board, after) && board.Get(after) == Stone.Empty ? 1 : 0);
            total += PatternScore(count, openEnds);
        }
        return total;
    }

    private static int PatternScore(int count, int openEnds) => (count, openEnds) switch
    {
        (4, 2) => 5_000,
        (4, 1) => 800,
        (3, 2) => 700,
        (3, 1) => 80,
        (2, 2) => 50,
        (2, 1) => 8,
        (1, 2) => 2,
        (1, 1) => 1,
        _ => 0
    };

    private static bool Contains(IReadOnlyBoard board, Position position) =>
        position.Row >= 0 && position.Row < board.Size &&
        position.Column >= 0 && position.Column < board.Columns;
}
