using System.Buffers.Binary;
using System.Text;

namespace RepViewer.Core;

public sealed record ReplayCompatibilityStatus(bool IsKnownLayout, bool IsLocalizedExecutableReplay, int? ChecksumOffset, uint? RecordedExeSize, uint? RecordedExeChecksum);

/// <summary>Diagnoses the TH07/08/09 executable checksum compatibility issue documented by thhylR.</summary>
public static class ReplayCompatibility
{
    private sealed record Signature(int Offset, uint ExeSize, uint ExeChecksum, string Version);
    private static readonly IReadOnlyDictionary<string, Signature> Signatures = new Dictionary<string, Signature>(StringComparer.OrdinalIgnoreCase)
    {
        ["th07"] = new(0x84, 0x0009EE00, 0xAEC5445C, "0100b"),
        ["th08"] = new(0xbc, 0x000CD400, 0xA26861B9, "0100d"),
        ["th09"] = new(0x114, 0x000A7400, 0xABEE4C8F, "0150a")
    };

    public static ReplayCompatibilityStatus Analyze(string gameId, ReadOnlySpan<byte> decoded)
    {
        if (!Signatures.TryGetValue(gameId, out var signature) || decoded.Length < signature.Offset + 13)
            return new ReplayCompatibilityStatus(false, false, null, null, null);
        var version = Encoding.ASCII.GetString(decoded.Slice(signature.Offset + 8, 5));
        var size = BinaryPrimitives.ReadUInt32LittleEndian(decoded.Slice(signature.Offset, 4));
        var checksum = BinaryPrimitives.ReadUInt32LittleEndian(decoded.Slice(signature.Offset + 4, 4));
        var localized = version == signature.Version && (size != signature.ExeSize || checksum != signature.ExeChecksum);
        return new ReplayCompatibilityStatus(true, localized, signature.Offset, size, checksum);
    }
}
