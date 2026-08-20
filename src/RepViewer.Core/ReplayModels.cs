using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Globalization;

namespace RepViewer.Core;

[Flags]
public enum ReplayKey
{
    None = 0,
    Up = 1 << 0,
    Down = 1 << 1,
    Left = 1 << 2,
    Right = 1 << 3,
    Z = 1 << 4,
    X = 1 << 5,
    Shift = 1 << 6,
    C = 1 << 7,
    V = 1 << 8,
    Ctrl = 1 << 9,
    D = 1 << 10
}

public enum ReplayNodeType { Value, Group, Array }

public enum ReplayIssueSeverity { Information, Warning, Error }

public sealed record ReplayIssue(string Code, ReplayIssueSeverity Severity, string Message, int? Offset = null);

public sealed record ReplayIdentity(string GameId, string FormatId, bool IsTrial = false);

/// <summary>A field after binary decoding and game-specific semantic conversion.</summary>
public sealed class ReplayPropertyNode
{
    public string Name { get; init; } = "";
    public ReplayNodeType Kind { get; init; }
    public object? SemanticValue { get; init; }
    public object? RawValue { get; init; }
    public string SourceFieldName { get; init; } = "";
    public int? Offset { get; init; }
    public bool IsUnknown { get; init; }
    public List<ReplayPropertyNode> Children { get; } = [];

    public int ArrayLength => SemanticValue switch
    {
        System.Collections.ICollection collection => collection.Count,
        _ => Children.Count
    };
}

/// <summary>
/// A value emitted by a format when its semantic value differs from the stored value.
/// Presentation/localization is deliberately not represented here.
/// </summary>
public sealed record SemanticField(object? Value, object? RawValue, string? SourceFieldName = null, int? Offset = null)
{
    public override string ToString() => Value?.ToString() ?? "";
}

public sealed record RawArrayField(object Values);

public sealed class UnknownField
{
    public int Offset { get; init; }
    public int Length => Data.Length;
    public byte[] Data { get; init; } = [];
    public Dictionary<string, object?> KnownValues { get; } = [];
    public List<UnknownValue> UnknownValues { get; } = [];
    public override string ToString() => "...";
}

public sealed record UnknownValue(string Name, int Offset, byte[] Data, string Kind = "DWORD")
{
    public string Hex => UnknownValueFormatter.Hex(Data);
    public string Int32 => UnknownValueFormatter.Int32(Data);
    public string Float => UnknownValueFormatter.Float(Data);
    public string Int16 => UnknownValueFormatter.Int16(Data);
    public string Int8 => UnknownValueFormatter.Int8(Data);
}

public static class UnknownValueFormatter
{
    public static string Hex(ReadOnlySpan<byte> bytes) => $"0x{Convert.ToHexString(bytes.ToArray().AsEnumerable().Reverse().ToArray())}";
    public static string Int32(ReadOnlySpan<byte> bytes)
    {
        Span<byte> padded = stackalloc byte[4];
        bytes[..Math.Min(4, bytes.Length)].CopyTo(padded);
        return BinaryPrimitives.ReadInt32LittleEndian(padded).ToString(CultureInfo.InvariantCulture);
    }
    public static string Float(ReadOnlySpan<byte> bytes)
    {
        Span<byte> padded = stackalloc byte[4];
        bytes[..Math.Min(4, bytes.Length)].CopyTo(padded);
        return BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(padded)).ToString("0.0################", CultureInfo.InvariantCulture);
    }
    public static string Int16(ReadOnlySpan<byte> bytes)
    {
        var values = new List<string>();
        for (var index = 0; index < bytes.Length; index += 2)
        {
            Span<byte> padded = new byte[2];
            bytes.Slice(index, Math.Min(2, bytes.Length - index)).CopyTo(padded);
            values.Add(BinaryPrimitives.ReadInt16LittleEndian(padded).ToString(CultureInfo.InvariantCulture));
        }
        return string.Join(", ", values);
    }
    public static string Int8(ReadOnlySpan<byte> bytes) => string.Join(", ", bytes.ToArray().Select(x => unchecked((sbyte)x).ToString(CultureInfo.InvariantCulture)));
}

public sealed class ReplayStage
{
    public int StageId { get; init; }
    public Dictionary<string, object?> Fields { get; init; } = [];
    public List<short> RawKeys { get; init; } = [];
    public List<ReplayKey> Keys { get; init; } = [];
    public List<byte>? Fps { get; set; }
    public List<byte>? RawFps { get; set; }
    public int? FpsIntervalFrames => Fps is null ? null : 30;
    public ReplayStatistics Statistics => ReplayKeyStatistics.Analyze(Keys);
    internal object? RawHeader { get; init; }
    public int? DecodedOffset { get; init; }
}

public sealed class ReplayStatistics
{
    public int[][] Matrix { get; init; } = CreateMatrix();
    public List<int>[][] Frames { get; init; } = CreateFrameMatrix();
    public static int[][] CreateMatrix() => Enumerable.Range(0, 16).Select(_ => new int[16]).ToArray();
    public static List<int>[][] CreateFrameMatrix() => Enumerable.Range(0, 16).Select(_ => Enumerable.Range(0, 16).Select(_ => new List<int>()).ToArray()).ToArray();
}

public static class ReplayKeyStatistics
{
    public static ReplayStatistics Analyze(IReadOnlyList<ReplayKey> keys)
    {
        var matrix = ReplayStatistics.CreateMatrix();
        var frames = ReplayStatistics.CreateFrameMatrix();
        for (var frame = 0; frame + 1 < keys.Count; frame++)
        {
            var from = (int)keys[frame] & 0xf;
            var to = (int)keys[frame + 1] & 0xf;
            matrix[from][to]++;
            frames[from][to].Add(frame + 1);
        }
        return new ReplayStatistics { Matrix = matrix, Frames = frames };
    }
}

public static class ReplayFrameTime
{
    public static TimeSpan FromFrames(int frames, double framesPerSecond = 60) => TimeSpan.FromSeconds(frames / framesPerSecond);

    public static string Format(int frames, int framesPerSecond = 60)
    {
        if (framesPerSecond < 1) throw new ArgumentOutOfRangeException(nameof(framesPerSecond));
        var negative = frames < 0;
        var value = Math.Abs((long)frames);
        var frame = value % framesPerSecond;
        var totalSeconds = value / framesPerSecond;
        var second = totalSeconds % 60;
        var totalMinutes = totalSeconds / 60;
        var minute = totalMinutes % 60;
        var hour = totalMinutes / 60;
        var parts = new List<string>();
        if (hour > 0) parts.Add(hour.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (minute > 0) parts.Add(minute.ToString(System.Globalization.CultureInfo.InvariantCulture));
        parts.Add(second.ToString("00", System.Globalization.CultureInfo.InvariantCulture));
        parts.Add(frame.ToString("00", System.Globalization.CultureInfo.InvariantCulture));
        return (negative ? "-" : "") + string.Join(':', parts);
    }
}
