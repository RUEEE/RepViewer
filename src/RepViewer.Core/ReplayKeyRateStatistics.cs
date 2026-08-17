using System.Numerics;

namespace RepViewer.Core;

public sealed class DirectionPunishmentTable
{
    private readonly float[,] _values = new float[9, 9];

    public float this[int previousDirection, int nextDirection]
    {
        get => _values[ToIndex(previousDirection), ToIndex(nextDirection)];
        set
        {
            var previous = ToIndex(previousDirection);
            var next = ToIndex(nextDirection);
            _values[previous, next] = value;
            _values[next, previous] = value;
        }
    }

    public static DirectionPunishmentTable CreateDefault()
    {
        var table = new DirectionPunishmentTable();
        table.SetAllOne();

        foreach (var diagonal in new[] { 1, 3, 7, 9 })
            table[5, diagonal] = 1.1f;
        foreach (var cardinal in new[] { 2, 4, 6, 8 })
            table[5, cardinal] = 1f;

        table[2, 8] = 2f;
        table[4, 6] = 1f;

        table[1, 9] = 3f;
        table[3, 7] = 3f;
        table[1, 7] = 2f;
        table[3, 9] = 2f;
        table[1, 3] = 1f;
        table[7, 9] = 1f;

        table[4, 3] = 1.5f;
        table[4, 9] = 1.5f;
        table[6, 1] = 1.5f;
        table[6, 7] = 1.5f;

        table[2, 7] = 2.5f;
        table[2, 9] = 2.5f;
        table[8, 1] = 2.5f;
        table[8, 3] = 2.5f;

        table[2, 4] = 1.5f;
        table[2, 6] = 1.5f;
        table[4, 8] = 1.5f;
        table[6, 8] = 1.5f;
        return table;
    }

    public void SetAllOne()
    {
        for (var previous = 1; previous <= 9; previous++)
            for (var next = previous; next <= 9; next++)
                this[previous, next] = previous == next ? 0f : 1f;
    }

    public void ResetToDefault()
    {
        var defaults = CreateDefault();
        for (var previous = 0; previous < 9; previous++)
            for (var next = 0; next < 9; next++)
                _values[previous, next] = defaults._values[previous, next];
    }

    private static int ToIndex(int direction)
    {
        if (direction is < 1 or > 9)
            throw new ArgumentOutOfRangeException(nameof(direction));
        return direction - 1;
    }
}

public sealed class ReplayKeyRateSeries
{
    public IReadOnlyList<float> Aps { get; init; } = [];
    public IReadOnlyList<float> Dps { get; init; } = [];
    public IReadOnlyList<float> Dfps { get; init; } = [];
}

public static class ReplayKeyRateStatistics
{
    public static ReplayKeyRateSeries Analyze(
        string version,
        IReadOnlyList<ReplayKey> keys,
        DirectionPunishmentTable punishment,
        int framesPerSecond = 60)
    {
        if (framesPerSecond < 1)
            throw new ArgumentOutOfRangeException(nameof(framesPerSecond));

        var actions = new float[keys.Count];
        var directions = new float[keys.Count];
        var directionsWithFocus = new float[keys.Count];

        for (var frame = 1; frame < keys.Count; frame++)
        {
            var previous = keys[frame - 1];
            var current = keys[frame];
            actions[frame] = BitOperations.PopCount((uint)(previous ^ current));

            var previousDirection = ResolveDirection(version, previous);
            var currentDirection = ResolveDirection(version, current);
            directions[frame] = punishment[previousDirection, currentDirection];
            directionsWithFocus[frame] = directions[frame] +
                (((previous ^ current) & ReplayKey.Shift) != 0 ? 1f : 0f);
        }

        return new ReplayKeyRateSeries
        {
            Aps = RollingSum(actions, framesPerSecond),
            Dps = RollingSum(directions, framesPerSecond),
            Dfps = RollingSum(directionsWithFocus, framesPerSecond)
        };
    }

    public static int ResolveDirection(string version, ReplayKey keys)
    {
        var up = (keys & ReplayKey.Up) != 0;
        var down = (keys & ReplayKey.Down) != 0;
        var left = (keys & ReplayKey.Left) != 0;
        var right = (keys & ReplayKey.Right) != 0;
        var earlyOverride = version is "TH06" or "TH07";

        if (up && down)
        {
            if (earlyOverride) down = false;
            else up = false;
        }
        if (left && right)
        {
            if (earlyOverride) left = false;
            else right = false;
        }

        if (up) return left ? 7 : right ? 9 : 8;
        if (down) return left ? 1 : right ? 3 : 2;
        if (left) return 4;
        if (right) return 6;
        return 5;
    }

    private static IReadOnlyList<float> RollingSum(IReadOnlyList<float> values, int windowFrames)
    {
        var result = new float[values.Count];
        var sum = 0f;
        for (var frame = 0; frame < values.Count; frame++)
        {
            sum += values[frame];
            if (frame >= windowFrames) sum -= values[frame - windowFrames];
            result[frame] = sum;
        }
        return result;
    }
}

