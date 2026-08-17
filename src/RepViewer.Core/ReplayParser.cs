namespace RepViewer.Core;

public static class ReplayParser
{
    public static ReplayDocument ParseFile(string path) => Parse(File.ReadAllBytes(path));

    public static byte[] DecodeFileForAnalysis(string path) => DecodeForAnalysis(File.ReadAllBytes(path));

    public static ReplayDocument Parse(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < 0x24)
            throw new InvalidDataException("Replay file is too short.");

        var magic = ReplayDecoder.U32(buffer, 0);
        var candidates = ReplayFormats.ForMagic(magic);
        if (candidates.Count == 0)
            throw new NotSupportedException($"Unsupported replay magic: 0x{magic:x8}.");

        Exception? last = null;
        foreach (var format in candidates)
        {
            try
            {
                var replay = format.Parse(buffer);
                var compatibility = ReplayCompatibility.Analyze(replay.Identity.GameId, format.DecodeForAnalysis(buffer));
                if (compatibility.IsLocalizedExecutableReplay)
                    replay.Issues.Add(new ReplayIssue("localized-executable-checksum", ReplayIssueSeverity.Warning,
                        "Replay was recorded by a modified executable and may be rejected by the original game.", compatibility.ChecksumOffset));
                return replay;
            }
            catch (Exception ex) { last = ex; }
        }
        throw new InvalidDataException("Replay data could not be parsed with the detected format.", last);
    }

    public static byte[] DecodeForAnalysis(ReadOnlySpan<byte> buffer)
    {
        var magic = ReplayDecoder.U32(buffer, 0);
        var formats = ReplayFormats.ForMagic(magic);
        if (formats.Count == 0) throw new NotSupportedException($"Unsupported replay magic: 0x{magic:x8}.");
        return formats[0].DecodeForAnalysis(buffer);
    }

}
