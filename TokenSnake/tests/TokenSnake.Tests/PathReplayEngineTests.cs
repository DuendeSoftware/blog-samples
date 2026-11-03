using TokenSnake.Game;
using Xunit;

namespace TokenSnake.Tests;

public class PathReplayEngineTests
{
    private static readonly GameRules Rules = GameRules.Default;
    private static IReadOnlyList<Cell> Start => Rules.CreateStartBody(); // head (10,10), heading right, tail (8,10)

    private static ReplayResult Replay(string moves, Cell pellet, IReadOnlyList<Cell>? body = null, Direction dir = Direction.Right) =>
        PathReplayEngine.Replay(body ?? Start, dir, moves, Rules, pellet);

    [Fact]
    public void Valid_path_moves_the_snake_and_grows_it()
    {
        var r = Replay("RUU", new Cell(11, 8));
        Assert.True(r.Ok);
        Assert.Equal(new Cell(11, 8), r.Body![0]);
        Assert.Equal(4, r.Body.Count); // ate: +1
        Assert.Equal(Direction.Up, r.Direction);
    }

    [Fact]
    public void Self_collision_is_detected()
    {
        // Length-6 snake going right; U, L, D runs into its own body.
        var body = Enumerable.Range(0, 6).Select(i => new Cell(10 - i, 10)).ToList();
        var r = Replay("ULDL", new Cell(0, 0), body);
        Assert.Equal(PathFailure.SelfCollision, r.FailureCode);
    }

    [Fact]
    public void Fatal_last_step_is_a_collision_not_a_wrong_endpoint()
    {
        var body = Enumerable.Range(0, 6).Select(i => new Cell(10 - i, 10)).ToList();
        Assert.Equal(PathFailure.SelfCollision, Replay("ULD", new Cell(0, 0), body).FailureCode);
        Assert.Equal(PathFailure.Wall, Replay(new string('R', 10), new Cell(0, 0)).FailureCode); // x = 20 on the last step
    }

    [Fact]
    public void Chasing_the_tail_is_legal_but_eating_into_it_is_not()
    {
        // 2x2 loop heading right: head (5,5), tail (5,6) directly below it.
        var body = new[] { new Cell(5, 5), new Cell(4, 5), new Cell(4, 6), new Cell(5, 6) };
        var ok = PathReplayEngine.Replay(body, Direction.Right, "DR", Rules, new Cell(6, 6));
        Assert.True(ok.Ok); // the tail has moved away by the time the head arrives
        Assert.Equal(new Cell(6, 6), ok.Body![0]);

        // If that step eats, the snake grows and the tail stays: the head bites it.
        var bite = PathReplayEngine.Replay(body, Direction.Right, "D", Rules, new Cell(5, 6));
        Assert.Equal(PathFailure.SelfCollision, bite.FailureCode);
    }

    [Fact]
    public void Wall_collision_is_detected()
    {
        // Pellet is elsewhere, so the only thing in the way is the wall (x = 20 after 10 steps right from x = 10).
        var r = Replay(new string('R', 12), new Cell(0, 0));
        Assert.Equal(PathFailure.Wall, r.FailureCode);
    }

    [Fact]
    public void Reversal_is_rejected()
    {
        Assert.Equal(PathFailure.Reversal, Replay("L", new Cell(0, 0)).FailureCode);
        Assert.Equal(PathFailure.Reversal, Replay("UD", new Cell(0, 0)).FailureCode);
    }

    [Fact]
    public void Wrong_endpoint_is_rejected()
    {
        Assert.Equal(PathFailure.WrongEndpoint, Replay("RR", new Cell(0, 0)).FailureCode);   // never eats
        Assert.Equal(PathFailure.WrongEndpoint, Replay("RRR", new Cell(11, 10)).FailureCode); // passes over the pellet early
    }

    [Fact]
    public void Oversized_input_is_rejected()
    {
        var r = Replay(new string('R', Rules.Cells * 4 + 1), new Cell(0, 0));
        Assert.Equal(PathFailure.BadInput, r.FailureCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("RX")]
    [InlineData("r")]
    [InlineData("R R")]
    [InlineData("R\n")]
    public void Bad_characters_and_empty_are_rejected(string moves) =>
        Assert.Equal(PathFailure.BadInput, Replay(moves, new Cell(11, 10)).FailureCode);

    [Theory]
    [InlineData(PathFailure.Wall, true)]
    [InlineData(PathFailure.SelfCollision, true)]
    [InlineData(PathFailure.Reversal, false)]
    [InlineData(PathFailure.WrongEndpoint, false)]
    [InlineData(PathFailure.BadInput, false)]
    public void Only_wall_and_self_are_honest_collisions(string code, bool collision) =>
        Assert.Equal(collision, PathFailure.IsCollision(code));

    [Fact]
    public void Path_hasher_chains()
    {
        var a = PathHasher.Next(PathHasher.Genesis, "R");
        Assert.NotEqual(a, PathHasher.Next(a, "R"));
        Assert.Equal(a, PathHasher.Next(PathHasher.Genesis, "R"));
    }
}
