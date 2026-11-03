namespace TokenSnake.Game;

/// <summary>Difficulty: more pellets, faster snake, shorter-lived tokens.</summary>
public static class LevelRules
{
    public const int PelletsPerLevel = 5;
    public const int PointsPerLevel = 10;

    // Index 0 = level 1. Levels past the end reuse the last entry.
    private static readonly int[] TickMs = [200, 170, 145, 125, 110, 100, 90, 80];
    private static readonly int[] LifetimeSeconds = [60, 45, 30, 20];

    public static int LevelFor(int pellets) => 1 + pellets / PelletsPerLevel;

    public static int TickMsFor(int level) => TickMs[Math.Min(level, TickMs.Length) - 1];

    public static int TokenLifetimeSecondsFor(int level) =>
        LifetimeSeconds[Math.Min(level, LifetimeSeconds.Length) - 1];

    public static int PointsFor(int level) => PointsPerLevel * level;
}
