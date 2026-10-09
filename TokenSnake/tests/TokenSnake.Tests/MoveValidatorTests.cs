using Microsoft.Extensions.Time.Testing;
using TokenSnake.Game;
using Xunit;

namespace TokenSnake.Tests;

public class MoveValidatorTests
{
    private static readonly GameRules Rules = GameRules.Default;
    private readonly MoveValidator _validator = new(Rules);
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UnixEpoch.AddHours(1));
    private static readonly DateTimeOffset Never = DateTimeOffset.MaxValue;

    private GameState Fresh(int seed = 1) => T.NewState(seed, now: _time.GetUtcNow());

    private EatResult Eat(GameState s, long seq, int idx, string moves, DateTimeOffset? expiry = null) =>
        _validator.Validate(s, new EatEvent(seq, idx, moves), expiry ?? Never, _time.GetUtcNow());

    [Fact]
    public void Valid_eat_scores_grows_and_advances_state()
    {
        var s = Fresh();
        var moves = T.PathToPellet(s);
        _time.Advance(TimeSpan.FromMilliseconds(250 * moves.Length));
        var r = Eat(s, 1, 0, moves);

        Assert.Equal(EatOutcome.Accepted, r.Outcome);
        Assert.Equal(10, r.State.Score);
        Assert.Equal(1, r.State.Seq);
        Assert.Equal(1, r.State.Pellets);
        Assert.Equal(4, r.State.Length);
        Assert.Equal(PathHasher.Next(PathHasher.Genesis, moves), r.State.PathHash);
        Assert.Equal(_time.GetUtcNow(), r.State.LastEatAt);
    }

    [Fact]
    public void Too_many_moves_for_elapsed_time_is_cheating()
    {
        var s = T.FindState(x => T.PathToPellet(x).Length > 6);
        var r = _validator.Validate(s, new EatEvent(1, 0, T.PathToPellet(s)), Never, s.LastEatAt); // zero elapsed time
        Assert.Equal(EatOutcome.GameOver, r.Outcome);
        Assert.Equal("cheat_detected:too_fast", r.Reason);
        Assert.Equal(GameStatus.Over, r.State.Status);
    }

    [Fact]
    public void Wrong_pellet_index_is_rejected_without_ending_game()
    {
        var s = Fresh();
        var r = Eat(s, 1, 5, "R");
        Assert.Equal(EatOutcome.Rejected, r.Outcome);
        Assert.Equal(RejectReason.BadPelletIndex, r.Reason);
        Assert.Equal(GameStatus.Active, r.State.Status);
    }

    [Theory]
    [InlineData(0L)]  // replayed
    [InlineData(2L)]  // skipped
    public void Replayed_or_skipped_seq_is_rejected_without_ending_game(long seq)
    {
        var s = Fresh();
        var r = Eat(s, seq, 0, "R");
        Assert.Equal(EatOutcome.Rejected, r.Outcome);
        Assert.Equal(RejectReason.BadSeq, r.Reason);
        Assert.Equal(GameStatus.Active, r.State.Status);
    }

    [Fact]
    public void Expired_token_ends_the_game()
    {
        var s = Fresh();
        var moves = T.PathToPellet(s);
        _time.Advance(TimeSpan.FromSeconds(10));
        var r = Eat(s, 1, 0, moves, expiry: _time.GetUtcNow().AddSeconds(-1));
        Assert.Equal(EatOutcome.GameOver, r.Outcome);
        Assert.Equal(RejectReason.TokenExpired, r.Reason);
    }

    [Fact]
    public void Game_already_over_is_rejected()
    {
        var s = Fresh() with { Status = GameStatus.Over };
        var r = Eat(s, 1, 0, "R");
        Assert.Equal(EatOutcome.Rejected, r.Outcome);
        Assert.Equal(RejectReason.GameNotActive, r.Reason);
    }

    [Fact]
    public void Wall_hit_is_a_collision_game_over()
    {
        var s = Fresh();
        _time.Advance(TimeSpan.FromSeconds(30));
        var r = Eat(s, 1, 0, T.WallPath(s));
        Assert.Equal(EatOutcome.GameOver, r.Outcome);
        Assert.Equal(RejectReason.Collision, r.Reason);
        Assert.Equal(GameStatus.Over, r.State.Status);
    }

    [Fact]
    public void Fatal_final_step_is_a_collision_game_over()
    {
        var s = Fresh();
        _time.Advance(TimeSpan.FromSeconds(30));
        var r = Eat(s, 1, 0, new string('R', 10)); // head hits the wall on the very last move
        Assert.Equal(EatOutcome.GameOver, r.Outcome);
        Assert.Equal(RejectReason.Collision, r.Reason);
        Assert.Equal(GameStatus.Over, r.State.Status);
    }

    [Theory]
    [InlineData("L", "reversal")]
    [InlineData("", "bad_input")]
    [InlineData("RX", "bad_input")]
    [InlineData("U", "wrong_endpoint")] // pellet is never one cell above the head for this seed (asserted below)
    public void Tampered_paths_end_the_game_with_cheat_code(string moves, string code)
    {
        var s = T.FindState(x => x.CurrentPellet(Rules) != new Cell(10, 9));
        _time.Advance(TimeSpan.FromSeconds(30));
        var r = Eat(s, 1, 0, moves);
        Assert.Equal(EatOutcome.GameOver, r.Outcome);
        Assert.Equal("cheat_detected:" + code, r.Reason);
    }
}
