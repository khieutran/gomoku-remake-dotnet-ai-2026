using Gomoku.Core;

namespace Gomoku.Core.Tests;

public sealed class RuleEvaluatorCrossCheckTests
{
    private static readonly (int Row, int Column)[] Directions =
    [
        (0, 1),
        (1, 0),
        (1, 1),
        (1, -1)
    ];

    [Fact]
    public void RandomBoardsAgreeWithIndependentFiveCellWindowCheck()
    {
        var random = new Random(20261024);
        var winningSamples = 0;
        foreach (var size in new[] { 5, 6, 10, 20 })
        {
            for (var sample = 0; sample < 500; sample++)
            {
                var board = new Board(size);
                for (var row = 0; row < size; row++)
                {
                    for (var column = 0; column < size; column++)
                    {
                        var stone = random.Next(3) switch
                        {
                            0 => Stone.Empty,
                            1 => Stone.X,
                            _ => Stone.O
                        };
                        if (stone != Stone.Empty)
                        {
                            Assert.True(board.TryPlace(new Position(row, column), stone));
                        }
                    }
                }

                var lastMove = new Position(random.Next(size), random.Next(size));
                var snapshot = board.Snapshot();
                var mover = snapshot.Get(lastMove);
                if (mover == Stone.Empty)
                {
                    mover = random.Next(2) == 0 ? Stone.X : Stone.O;
                    Assert.True(board.TryPlace(lastMove, mover));
                    snapshot = board.Snapshot();
                }

                var expected = EvaluateByWindows(snapshot, lastMove, mover);
                if (expected is GameStatus.XWon or GameStatus.OWon)
                {
                    winningSamples++;
                }

                var actual = RuleEvaluator.Evaluate(snapshot, lastMove, mover, new RuleSet(size));
                Assert.True(expected == actual,
                    $"size={size}, sample={sample}, last={lastMove}, mover={mover}, expected={expected}, actual={actual}");
            }
        }

        Assert.True(winningSamples > 0, "The generated boards must include winning positions.");
    }

    private static GameStatus EvaluateByWindows(BoardSnapshot board, Position lastMove, Stone mover)
    {
        var opponent = mover == Stone.X ? Stone.O : Stone.X;
        foreach (var (rowStep, columnStep) in Directions)
        {
            for (var offset = -4; offset <= 0; offset++)
            {
                var first = new Position(lastMove.Row + offset * rowStep,
                    lastMove.Column + offset * columnStep);
                var fifth = new Position(first.Row + 4 * rowStep, first.Column + 4 * columnStep);
                if (!Contains(board, first) || !Contains(board, fifth))
                {
                    continue;
                }

                var allFive = Enumerable.Range(0, 5).All(index =>
                    board.Get(new Position(first.Row + index * rowStep, first.Column + index * columnStep)) == mover);
                if (!allFive)
                {
                    continue;
                }

                var before = new Position(first.Row - rowStep, first.Column - columnStep);
                var after = new Position(fifth.Row + rowStep, fifth.Column + columnStep);
                if (HasStone(board, before, mover) || HasStone(board, after, mover))
                {
                    continue;
                }

                if (!HasStone(board, before, opponent) || !HasStone(board, after, opponent))
                {
                    return mover == Stone.X ? GameStatus.XWon : GameStatus.OWon;
                }
            }
        }

        for (var row = 0; row < board.Size; row++)
        {
            for (var column = 0; column < board.Size; column++)
            {
                if (board.Get(new Position(row, column)) == Stone.Empty)
                {
                    return GameStatus.InProgress;
                }
            }
        }

        return GameStatus.Draw;
    }

    private static bool Contains(BoardSnapshot board, Position position) =>
        position.Row >= 0 && position.Row < board.Size &&
        position.Column >= 0 && position.Column < board.Size;

    private static bool HasStone(BoardSnapshot board, Position position, Stone stone) =>
        Contains(board, position) && board.Get(position) == stone;
}
