namespace TokenSnake.Game;

/// <summary>Reason codes for rejections that don't touch the game (stale or malformed requests).</summary>
public static class RejectReason
{
    public const string GameNotActive = "game_not_active";
    public const string BadSeq = "bad_seq";
    public const string BadPelletIndex = "bad_pellet_index";
    public const string TokenExpired = "token_expired";
    public const string TooFast = "too_fast";
    public const string CheatPrefix = "cheat_detected:";
    public const string Collision = "collision";
}

/// <summary>What the client claims: "I ate pellet N with these moves."</summary>
public sealed record EatEvent(long Seq, int PelletIndex, string Moves);

public enum EatOutcome
{
    /// <summary>Valid eat; <see cref="EatResult.State"/> is the next state to store.</summary>
    Accepted,
    /// <summary>Request ignored; the stored game is untouched (stale/replayed request).</summary>
    Rejected,
    /// <summary>The game is over; <see cref="EatResult.State"/> is the final state to store.</summary>
    GameOver,
}

public sealed record EatResult(EatOutcome Outcome, string? Reason, GameState State);

public sealed class MoveValidator(GameRules rules)
{
    /// <summary>Latency and timer jitter: 25% extra moves plus a few free ones.</summary>
    public const double TimingTolerance = 0.25;
    public const int TimingSlackMoves = 3;

    public EatResult Validate(GameState state, EatEvent evt, DateTimeOffset tokenExpiry, DateTimeOffset now)
    {
        // Cheap, state-only checks first; none of these end the game.
        if (state.Status != GameStatus.Active) return Reject(state, RejectReason.GameNotActive);
        if (evt.Seq != state.Seq + 1) return Reject(state, RejectReason.BadSeq);
        if (evt.PelletIndex != state.Pellets) return Reject(state, RejectReason.BadPelletIndex);

        if (now > tokenExpiry) return End(state, RejectReason.TokenExpired);

        // Never trust a client-sent position: only the replayed endpoint counts.
        var pellet = state.CurrentPellet(rules);
        var replay = PathReplayEngine.Replay(state.Body, state.Direction, evt.Moves, rules, pellet);
        if (!replay.Ok)
            return End(state, PathFailure.IsCollision(replay.FailureCode!)
                ? RejectReason.Collision
                : RejectReason.CheatPrefix + replay.FailureCode);

        if (!TimingPlausible(state, evt.Moves.Length, now))
            return End(state, RejectReason.CheatPrefix + RejectReason.TooFast);

        return Accept(state, evt, replay, now);
    }

    /// <summary>A snake can't make more moves than the elapsed time allows at this level's tick rate.</summary>
    private static bool TimingPlausible(GameState state, int moves, DateTimeOffset now)
    {
        var elapsedMs = (now - state.LastEatAt).TotalMilliseconds;
        var maxMoves = elapsedMs / LevelRules.TickMsFor(state.Level) * (1 + TimingTolerance) + TimingSlackMoves;
        return moves <= maxMoves;
    }

    private static EatResult Accept(GameState state, EatEvent evt, ReplayResult replay, DateTimeOffset now)
    {
        var next = state with
        {
            Body = replay.Body!,
            Direction = replay.Direction,
            Pellets = state.Pellets + 1,
            Score = state.Score + LevelRules.PointsFor(state.Level),
            PathHash = PathHasher.Next(state.PathHash, evt.Moves),
            LastEatAt = now,
            Seq = evt.Seq,
        };
        return new EatResult(EatOutcome.Accepted, null, next);
    }

    private static EatResult Reject(GameState state, string reason) => new(EatOutcome.Rejected, reason, state);

    private static EatResult End(GameState state, string reason) =>
        new(EatOutcome.GameOver, reason, state with { Status = GameStatus.Over });
}
