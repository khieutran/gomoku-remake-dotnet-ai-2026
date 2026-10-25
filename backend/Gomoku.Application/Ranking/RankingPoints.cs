using Gomoku.Core;

namespace Gomoku.Application.Ranking;

public static class RankingPoints
{
    public static int For(GameStatus status, Stone humanStone) => status switch
    {
        GameStatus.Draw => 1,
        GameStatus.XWon when humanStone == Stone.X => 3,
        GameStatus.OWon when humanStone == Stone.O => 3,
        _ => 0
    };
}
