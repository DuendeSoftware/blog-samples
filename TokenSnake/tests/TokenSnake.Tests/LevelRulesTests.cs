using TokenSnake.Game;
using Xunit;

namespace TokenSnake.Tests;

public class LevelRulesTests
{
    [Theory]
    [InlineData(0, 1)] [InlineData(4, 1)] [InlineData(5, 2)] [InlineData(9, 2)] [InlineData(10, 3)] [InlineData(100, 21)]
    public void Level_rises_every_five_pellets(int pellets, int level) => Assert.Equal(level, LevelRules.LevelFor(pellets));

    [Theory]
    [InlineData(1, 200)] [InlineData(2, 170)] [InlineData(8, 80)] [InlineData(9, 80)] [InlineData(50, 80)]
    public void Tick_speeds_up_then_plateaus(int level, int ms) => Assert.Equal(ms, LevelRules.TickMsFor(level));

    [Theory]
    [InlineData(1, 60)] [InlineData(2, 45)] [InlineData(3, 30)] [InlineData(4, 20)] [InlineData(5, 20)] [InlineData(99, 20)]
    public void Token_lifetime_shrinks_then_plateaus(int level, int seconds) =>
        Assert.Equal(seconds, LevelRules.TokenLifetimeSecondsFor(level));

    [Theory]
    [InlineData(1, 10)] [InlineData(3, 30)]
    public void Points_scale_with_level(int level, int points) => Assert.Equal(points, LevelRules.PointsFor(level));
}
