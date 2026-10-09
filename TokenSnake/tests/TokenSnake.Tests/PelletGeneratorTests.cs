using TokenSnake.Game;
using Xunit;

namespace TokenSnake.Tests;

public class PelletGeneratorTests
{
    private static readonly GameRules Rules = GameRules.Default;
    private static byte[] Seed(byte b) => Enumerable.Repeat(b, 32).ToArray();

    [Fact]
    public void Same_seed_index_and_body_give_same_pellet()
    {
        var body = Rules.CreateStartBody();
        for (var i = 0; i < 50; i++)
            Assert.Equal(PelletGenerator.Get(Seed(7), i, Rules, body), PelletGenerator.Get(Seed(7), i, Rules, body));
    }

    [Fact]
    public void Different_seeds_give_different_sequences()
    {
        var body = Rules.CreateStartBody();
        var a = Enumerable.Range(0, 20).Select(i => PelletGenerator.Get(Seed(1), i, Rules, body)).ToList();
        var b = Enumerable.Range(0, 20).Select(i => PelletGenerator.Get(Seed(2), i, Rules, body)).ToList();
        Assert.False(a.SequenceEqual(b));
    }

    [Fact]
    public void Pellets_stay_on_the_board_and_never_on_the_snake()
    {
        var body = Rules.CreateStartBody().Concat(
            Enumerable.Range(0, 15).Select(x => new Cell(x, 3))).Distinct().ToList();
        for (byte s = 1; s < 20; s++)
            for (var i = 0; i < 100; i++)
            {
                var p = PelletGenerator.Get(Seed(s), i, Rules, body);
                Assert.True(Rules.Contains(p));
                Assert.DoesNotContain(p, body);
            }
    }

    [Fact]
    public void Only_free_cell_is_found_and_full_board_throws()
    {
        var tiny = new GameRules(2, 2, 0, 0, Direction.Right, 1);
        var three = new[] { new Cell(0, 0), new Cell(1, 0), new Cell(0, 1) };
        Assert.Equal(new Cell(1, 1), PelletGenerator.Get(Seed(1), 0, tiny, three));
        Assert.Throws<InvalidOperationException>(() =>
            PelletGenerator.Get(Seed(1), 0, tiny, [.. three, new Cell(1, 1)]));
    }
}
