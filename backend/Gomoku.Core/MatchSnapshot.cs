namespace Gomoku.Core;

public sealed class MatchSnapshot
{
    internal MatchSnapshot(int xScore, int oScore, Stone openingStone, GameSnapshot currentGame,
        IReadOnlyList<GameSnapshot> completedGames)
    {
        XScore = xScore;
        OScore = oScore;
        OpeningStone = openingStone;
        CurrentGame = currentGame;
        CompletedGames = completedGames;
    }

    public int XScore { get; }
    public int OScore { get; }
    public Stone OpeningStone { get; }
    public GameSnapshot CurrentGame { get; }
    public IReadOnlyList<GameSnapshot> CompletedGames { get; }
}
