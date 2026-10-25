using Gomoku.Application.Ranking;
using Gomoku.Core;
using Xunit;

namespace Gomoku.Application.Tests.Ranking;

public sealed class RankingPointsTests
{
    [Theory]
    [InlineData(GameStatus.XWon, Stone.X, 3)]
    [InlineData(GameStatus.OWon, Stone.X, 0)]
    [InlineData(GameStatus.XWon, Stone.O, 0)]
    [InlineData(GameStatus.OWon, Stone.O, 3)]
    [InlineData(GameStatus.Draw, Stone.X, 1)]
    [InlineData(GameStatus.Draw, Stone.O, 1)]
    [InlineData(GameStatus.InProgress, Stone.X, 0)]
    public void AwardsOnlyTheHumanResult(GameStatus status, Stone humanStone, int expected)
    {
        Assert.Equal(expected, RankingPoints.For(status, humanStone));
    }
}
