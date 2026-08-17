using System.Text;

namespace RepViewer.Core;

/// <summary>A compact, locale-neutral projection intended for the main window and Shell metadata.</summary>
public sealed record ReplaySummary(
    ReplayIdentity Identity,
    string? PlayerName,
    object? Score,
    object? Character,
    object? Difficulty,
    int StageCount,
    TimeSpan Duration,
    string? Comment)
{
    public static ReplaySummary Create(ReplayDocument replay, Encoding textEncoding)
    {
        ArgumentNullException.ThrowIfNull(replay);
        ArgumentNullException.ThrowIfNull(textEncoding);
        return new ReplaySummary(
            replay.Identity,
            DecodeText(Value(replay.General, "Name") ?? Value(replay.General, "Player"), textEncoding),
            Value(replay.General, "TotalScore") ?? Value(replay.General, "Score"),
            Value(replay.General, "Character"),
            Value(replay.General, "Rank") ?? Value(replay.General, "Difficulty"),
            replay.Stages.Count,
            ReplayFrameTime.FromFrames(replay.Stages.Sum(stage => stage.Keys.Count)),
            replay.UserData?.DecodeComment(textEncoding));
    }

    private static object? Value(ReplayPropertyNode group, string name) =>
        group.Children.FirstOrDefault(node => node.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.SemanticValue;

    private static string? DecodeText(object? value, Encoding encoding)
    {
        if (value is string text) return text;
        if (value is not byte[] bytes) return value?.ToString();
        var end = Array.IndexOf(bytes, (byte)0);
        return encoding.GetString(end < 0 ? bytes : bytes[..end]).Trim();
    }
}
