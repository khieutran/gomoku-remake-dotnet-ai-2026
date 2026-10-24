namespace Gomoku.Core;

public sealed class RuleSet
{
    public const string CurrentVersion = "gomoku-exact-five-v1";

    public RuleSet(int boardSize)
    {
        if (boardSize < 5)
        {
            throw new ArgumentOutOfRangeException(nameof(boardSize), "A Gomoku board must have at least five rows and columns.");
        }

        BoardSize = boardSize;
    }

    public int BoardSize { get; }

    public string Version => CurrentVersion;
}
