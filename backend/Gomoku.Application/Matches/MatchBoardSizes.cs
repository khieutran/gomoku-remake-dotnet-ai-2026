namespace Gomoku.Application.Matches;

public static class MatchBoardSizes
{
    public static bool Supports(int size) => size is 20 or 30 or 40;
    public static bool Supports(int rows, int columns) =>
        rows == columns && Supports(rows) || rows == 20 && columns == 30;
}
