namespace Gomoku.Core;

public static class RuleEvaluator
{
    private static readonly (int Row, int Column)[] Directions =
    [
        (0, 1),
        (1, 0),
        (1, 1),
        (1, -1)
    ];

    public static GameStatus Evaluate(IReadOnlyBoard board, Position lastMove, Stone mover, RuleSet rules)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(rules);

        if (board.Size != rules.BoardSize || board.Columns != rules.BoardColumns)
        {
            throw new ArgumentException("Board size does not match the rule set.", nameof(board));
        }

        if (lastMove.Row < 0 || lastMove.Row >= board.Size || lastMove.Column < 0 || lastMove.Column >= board.Columns)
        {
            throw new ArgumentOutOfRangeException(nameof(lastMove));
        }

        if (mover is not (Stone.X or Stone.O) || board.Get(lastMove) != mover)
        {
            throw new ArgumentException("The last move must contain the mover's stone.", nameof(lastMove));
        }

        foreach (var (rowStep, columnStep) in Directions)
        {
            var backward = Count(board, lastMove, mover, -rowStep, -columnStep);
            var forward = Count(board, lastMove, mover, rowStep, columnStep);
            if (backward + 1 + forward != 5)
            {
                continue;
            }

            var opponent = mover == Stone.X ? Stone.O : Stone.X;
            var before = new Position(lastMove.Row - (backward + 1) * rowStep,
                lastMove.Column - (backward + 1) * columnStep);
            var after = new Position(lastMove.Row + (forward + 1) * rowStep,
                lastMove.Column + (forward + 1) * columnStep);

            if (IsOpponentAt(board, before, opponent) && IsOpponentAt(board, after, opponent))
            {
                continue;
            }

            return mover == Stone.X ? GameStatus.XWon : GameStatus.OWon;
        }

        for (var row = 0; row < board.Size; row++)
        {
            for (var column = 0; column < board.Columns; column++)
            {
                if (board.Get(new Position(row, column)) == Stone.Empty)
                {
                    return GameStatus.InProgress;
                }
            }
        }

        return GameStatus.Draw;
    }

    private static int Count(IReadOnlyBoard board, Position start, Stone stone, int rowStep, int columnStep)
    {
        var count = 0;
        var row = start.Row + rowStep;
        var column = start.Column + columnStep;

        while (row >= 0 && row < board.Size && column >= 0 && column < board.Columns &&
               board.Get(new Position(row, column)) == stone)
        {
            count++;
            row += rowStep;
            column += columnStep;
        }

        return count;
    }

    private static bool IsOpponentAt(IReadOnlyBoard board, Position position, Stone opponent) =>
        position.Row >= 0 && position.Row < board.Size &&
        position.Column >= 0 && position.Column < board.Columns &&
        board.Get(position) == opponent;
}
