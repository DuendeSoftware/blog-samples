namespace TokenSnake.Game;

/// <summary>Why a path was rejected. Values double as the wire reason codes.</summary>
public static class PathFailure
{
    public const string BadInput = "bad_input";
    public const string Reversal = "reversal";
    public const string Wall = "wall";
    public const string SelfCollision = "self_collision";
    public const string WrongEndpoint = "wrong_endpoint";

    /// <summary>Wall and self hits are honest deaths; everything else smells like tampering.</summary>
    public static bool IsCollision(string code) => code is Wall or SelfCollision;
}

public sealed record ReplayResult(
    IReadOnlyList<Cell>? Body,
    Direction Direction,
    string? FailureCode)
{
    public bool Ok => FailureCode is null;
}

/// <summary>
/// Replays the client's move string on the server's snake, one step at a time.
/// Pure: no clock, no state, no I/O.
/// </summary>
public static class PathReplayEngine
{
    public static ReplayResult Replay(
        IReadOnlyList<Cell> body, Direction direction, string moves, GameRules rules, Cell pellet)
    {
        // Cheap checks first: never loop over attacker-controlled input before validating it.
        if (!IsWellFormed(moves, rules))
            return Fail(PathFailure.BadInput, direction);

        var snake = new LinkedList<Cell>(body); // head = First
        var occupied = new HashSet<Cell>(body);

        for (var i = 0; i < moves.Length; i++)
        {
            var move = ToDirection(moves[i]);
            if (move == GameRules.Opposite(direction))
                return Fail(PathFailure.Reversal, direction);
            direction = move;

            var head = snake.First!.Value.Step(move);
            if (!rules.Contains(head))
                return Fail(PathFailure.Wall, direction);

            var eats = head == pellet;
            var isLast = i == moves.Length - 1;

            // The tail moves first (unless we grow), so chasing your own tail is legal.
            if (!eats)
            {
                occupied.Remove(snake.Last!.Value);
                snake.RemoveLast();
            }

            // Collisions come before the endpoint rule: a fatal final step is a death, not tampering.
            if (!occupied.Add(head))
                return Fail(PathFailure.SelfCollision, direction);

            if (eats != isLast)
                // Passing over the pellet early, or finishing elsewhere: the path doesn't match.
                return Fail(PathFailure.WrongEndpoint, direction);

            snake.AddFirst(head);
        }

        return new ReplayResult(snake.ToArray(), direction, null);
    }

    private static bool IsWellFormed(string moves, GameRules rules) =>
        moves.Length > 0
        && moves.Length <= rules.Cells * 4
        && moves.All(c => c is 'U' or 'D' or 'L' or 'R');

    private static Direction ToDirection(char c) => c switch
    {
        'U' => Direction.Up,
        'D' => Direction.Down,
        'L' => Direction.Left,
        _ => Direction.Right,
    };

    private static ReplayResult Fail(string code, Direction direction) => new(null, direction, code);
}
