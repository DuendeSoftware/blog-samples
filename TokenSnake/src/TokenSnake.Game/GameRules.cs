namespace TokenSnake.Game;

public enum Direction { Up, Down, Left, Right }

/// <summary>A grid cell. (0,0) is the top-left corner; Y grows downwards.</summary>
public readonly record struct Cell(int X, int Y)
{
    public Cell Step(Direction d) => d switch
    {
        Direction.Up => this with { Y = Y - 1 },
        Direction.Down => this with { Y = Y + 1 },
        Direction.Left => this with { X = X - 1 },
        _ => this with { X = X + 1 },
    };
}

/// <summary>
/// The single source of truth for the board. The server sends this record to the
/// browser in the <c>start</c> response, so client and server never disagree.
/// Walls are lethal: there is no wrap-around.
/// </summary>
public sealed record GameRules(int Width, int Height, int StartX, int StartY, Direction StartDirection, int StartLength)
{
    public static GameRules Default { get; } = new(20, 20, 10, 10, Direction.Right, 3);

    public int Cells => Width * Height;

    public bool Contains(Cell c) => c.X >= 0 && c.X < Width && c.Y >= 0 && c.Y < Height;

    /// <summary>Head first; the tail trails behind the head, opposite to the start direction.</summary>
    public IReadOnlyList<Cell> CreateStartBody()
    {
        var back = Opposite(StartDirection);
        var body = new List<Cell>(StartLength);
        var cell = new Cell(StartX, StartY);
        for (var i = 0; i < StartLength; i++)
        {
            body.Add(cell);
            cell = cell.Step(back);
        }
        return body;
    }

    public static Direction Opposite(Direction d) => d switch
    {
        Direction.Up => Direction.Down,
        Direction.Down => Direction.Up,
        Direction.Left => Direction.Right,
        _ => Direction.Left,
    };
}
