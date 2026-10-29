namespace Gomoku.Core;

public sealed class RuleSet
{
    public const string CurrentVersion = "gomoku-exact-five-v1";

    public RuleSet(int boardSize, int? boardColumns = null)
    {
        if (boardSize < 5)
        {
            throw new ArgumentOutOfRangeException(nameof(boardSize), "A Gomoku board must have at least five rows and columns.");
        }
        if ((boardColumns ?? boardSize) < 5)
            throw new ArgumentOutOfRangeException(nameof(boardColumns));

        BoardSize = boardSize;
        BoardColumns = boardColumns ?? boardSize;
    }

    public int BoardSize { get; }
    public int BoardColumns { get; }

    public string Version => CurrentVersion;
}
