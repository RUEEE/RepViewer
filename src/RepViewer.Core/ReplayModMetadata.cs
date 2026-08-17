using System.Text;
using System.Text.Json;

namespace RepViewer.Core;

/// <summary>
/// Raw mod-owned metadata. RawData is authoritative; Text and ParsedJson are optional interpretations.
/// </summary>
public sealed record ReplayModMetadata(int Offset, byte[] RawData, string? Text, JsonElement? ParsedJson)
{
    public static ReplayModMetadata? TryRead(ReadOnlySpan<byte> replay, ReplayUserData? userData = null)
    {
        if (userData is not null)
        {
            var blockOffset = userData.Offset;
            foreach (var block in userData.Blocks)
            {
                var marker = block.Data.AsSpan().IndexOf("PRAC"u8);
                if (marker >= 0)
                    return Create(blockOffset + 12 + marker, block.Data.AsSpan(marker).ToArray());
                blockOffset += block.EncodedLength;
            }
        }

        var offset = replay.LastIndexOf("PRAC"u8);
        return offset < 0 ? null : Create(offset, replay[offset..].ToArray());
    }

    private static ReplayModMetadata Create(int offset, byte[] raw)
    {
        var significantLength = raw.Length;
        while (significantLength > 0 && raw[significantLength - 1] == 0) significantLength--;
        var significant = raw.AsSpan(0, significantLength);
        string? text = null;
        try { text = new UTF8Encoding(false, true).GetString(significant); }
        catch (DecoderFallbackException) { }

        JsonElement? parsed = null;
        if (significant.StartsWith("PRAC{"u8))
        {
            var jsonLength = FindJsonObjectLength(significant[4..]);
            if (jsonLength > 0)
            {
                try
                {
                    using var document = JsonDocument.Parse(significant.Slice(4, jsonLength).ToArray());
                    parsed = document.RootElement.Clone();
                }
                catch (JsonException) { }
            }
        }
        return new ReplayModMetadata(offset, raw, text, parsed);
    }

    private static int FindJsonObjectLength(ReadOnlySpan<byte> json)
    {
        if (json.IsEmpty || json[0] != (byte)'{') return 0;
        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var index = 0; index < json.Length; index++)
        {
            var value = json[index];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (value == (byte)'\\') escaped = true;
                else if (value == (byte)'"') inString = false;
                continue;
            }
            if (value == (byte)'"') inString = true;
            else if (value == (byte)'{') depth++;
            else if (value == (byte)'}' && --depth == 0) return index + 1;
        }
        return 0;
    }
}
