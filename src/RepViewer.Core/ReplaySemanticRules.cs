using System.Globalization;

namespace RepViewer.Core;

internal static class ReplaySemanticRules
{
    private static readonly HashSet<string> RawScoreFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "TH06", "TH09.5", "TH12.5", "TH16.5", "alcostg"
    };

    public static object? Convert(string formatId, string fieldName, object? rawValue, int offset)
    {
        var canonicalFormat = formatId.EndsWith("Trial", StringComparison.OrdinalIgnoreCase) ? formatId[..^5] : formatId;
        if (canonicalFormat == "TH17" && fieldName == "Spirits" && rawValue is byte[] spiritBytes && spiritBytes.Length == 20)
        {
            var spirits = Enumerable.Range(0, 5)
                .Select(index => System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(spiritBytes.AsSpan(index * 4, 4))).ToArray();
            return new SemanticField(spirits, spirits, fieldName, offset);
        }
        if (rawValue is byte[] bytes && fieldName is "Name" or "Date" or "Magic")
        {
            var end = Array.IndexOf(bytes, (byte)0);
            var payload = end < 0 ? bytes : bytes[..end];
            string text;
            try { text = ReplayTextEncoding.ShiftJis.GetString(payload); }
            catch { text = System.Text.Encoding.UTF8.GetString(payload); }
            return new SemanticField(text.Trim(), bytes, fieldName, offset);
        }
        if ((fieldName.Equals("Score", StringComparison.OrdinalIgnoreCase) ||
             fieldName.Equals("TotalScore", StringComparison.OrdinalIgnoreCase)) &&
            !RawScoreFormats.Contains(canonicalFormat) && TryUnsigned(rawValue, out var score))
            return new SemanticField(score * 10UL, rawValue, fieldName, offset);
        if (fieldName.Equals("Timestamp", StringComparison.OrdinalIgnoreCase) && TryUnsigned(rawValue, out var timestamp) && timestamp <= uint.MaxValue)
            return new SemanticField(DateTimeOffset.FromUnixTimeSeconds((long)timestamp), rawValue, fieldName, offset);
        if (canonicalFormat == "TH08" && fieldName == "SpellNumber" && TrySigned(rawValue, out var spellNumber))
            return new SemanticField(spellNumber + 1L, rawValue, fieldName, offset);
        if (canonicalFormat == "TH08" && fieldName == "YoukaiRate" && TrySigned(rawValue, out var youkaiRate))
            return new SemanticField(youkaiRate / 100m, rawValue, fieldName, offset);
        if (canonicalFormat == "alcostg" && fieldName == "LastStage" && TryUnsigned(rawValue, out var lastStage))
            return new SemanticField((long)lastStage - 1L, rawValue, fieldName, offset);
        if (canonicalFormat == "alcostg" && fieldName == "NoDInput" && TryUnsigned(rawValue, out var inputFlags))
            return new SemanticField((inputFlags & 8UL) != 0, rawValue, fieldName, offset);
        if (canonicalFormat is "TH14.3" or "TH16.5" && fieldName is "Day" or "Scene" && TryUnsigned(rawValue, out var sceneIndex))
            return new SemanticField(sceneIndex + 1UL, rawValue, fieldName, offset);
        if (TryUnsigned(rawValue, out var raw))
        {
            if (canonicalFormat is "TH09.5" or "TH12.5" && fieldName is "LevelId" or "SubLevelId") return new SemanticField(raw + 1UL, rawValue, fieldName, offset);
            if (canonicalFormat == "TH10" && fieldName == "Faith") return new SemanticField(raw * 10UL, rawValue, fieldName, offset);
            if (canonicalFormat == "TH10" && fieldName == "FaithGauge") return new SemanticField(raw / 1.3m, rawValue, fieldName, offset);
            if (canonicalFormat == "TH10" && fieldName == "Power") return new SemanticField(raw / 20m, rawValue, fieldName, offset);
            if (canonicalFormat == "TH12.8" && fieldName == "FreezeLevel") return new SemanticField(raw + 1UL, rawValue, fieldName, offset);
            if (canonicalFormat == "TH12.8" && fieldName == "FreezePower") return new SemanticField(raw / 1000m, rawValue, fieldName, offset);
            if (canonicalFormat == "TH12.8" && fieldName is "Motivation" or "PerfectFreeze") return new SemanticField(raw / 100m, rawValue, fieldName, offset);
            if (fieldName == "Power" && canonicalFormat is "TH12" or "TH13" or "TH14" or "TH15" or "TH16" or "TH17" or "TH18" or "TH20")
                return new SemanticField(raw / 100m, rawValue, fieldName, offset);
            if (fieldName == "Piv" && canonicalFormat is "TH12" or "TH13" or "TH14" or "TH15" or "TH16" or "TH17")
                return new SemanticField(raw / 1000UL * 10UL, rawValue, fieldName, offset);
            if (fieldName == "Piv" && canonicalFormat == "TH20") return new SemanticField(raw / 5000m, rawValue, fieldName, offset);
            if (canonicalFormat == "TH13" && fieldName == "Trance") return new SemanticField(raw / 6m, rawValue, fieldName, offset);
        }
        return rawValue;
    }

    private static bool TryUnsigned(object? value, out ulong result)
    {
        try { result = System.Convert.ToUInt64(value, CultureInfo.InvariantCulture); return true; }
        catch { result = 0; return false; }
    }

    private static bool TrySigned(object? value, out long result)
    {
        try { result = System.Convert.ToInt64(value, CultureInfo.InvariantCulture); return true; }
        catch { result = 0; return false; }
    }
}
