using System.Buffers.Binary;
using System.Text;

namespace RepViewer.Core;

public enum ReplayUserBlockType { UserInfo = 0, Comment = 1 }

public sealed record ReplayUserBlock(string Marker, int Id, byte[] Data)
{
    public ReplayUserBlockType Type => (ReplayUserBlockType)(Id & 0xff);
    public int EncodedLength => checked(Data.Length + 12);
}

/// <summary>Trailing USER blocks used by thhylR for summaries and comments.</summary>
public sealed class ReplayUserData
{
    private ReplayUserData(int offset, IReadOnlyList<ReplayUserBlock> blocks)
    {
        Offset = offset;
        Blocks = blocks;
    }

    public int Offset { get; }
    public IReadOnlyList<ReplayUserBlock> Blocks { get; }
    public ReplayUserBlock? SummaryBlock => Blocks.FirstOrDefault(block => block.Marker == "USER" && block.Type == ReplayUserBlockType.UserInfo);
    public ReplayUserBlock? CommentBlock => Blocks.FirstOrDefault(block => block.Marker == "USER" && block.Type == ReplayUserBlockType.Comment);

    public string? DecodeSummary(Encoding encoding) => DecodeBlock(SummaryBlock, encoding);

    public string? DecodeComment(Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);
        return DecodeBlock(CommentBlock, encoding);
    }

    private static string? DecodeBlock(ReplayUserBlock? block, Encoding encoding)
    {
        var data = block?.Data;
        if (data is null) return null;
        var end = Array.IndexOf(data, (byte)0);
        return encoding.GetString(end < 0 ? data : data[..end]);
    }

    public byte[] WithComment(ReadOnlySpan<byte> original, string comment, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(comment);
        ArgumentNullException.ThrowIfNull(encoding);
        if (Offset < 0 || Offset > original.Length) throw new InvalidDataException("USER block offset is outside the replay.");
        var encoded = encoding.GetBytes(comment).Append((byte)0).ToArray();
        var replacement = new ReplayUserBlock("USER", (int)ReplayUserBlockType.Comment, encoded);
        var blocks = Blocks.ToList();
        var index = blocks.FindIndex(block => block.Marker == "USER" && block.Type == ReplayUserBlockType.Comment);
        if (index >= 0) blocks[index] = replacement; else blocks.Add(replacement);

        using var stream = new MemoryStream();
        stream.Write(original[..Offset]);
        foreach (var block in blocks) WriteBlock(stream, block);
        return stream.ToArray();
    }

    public static ReplayUserData? TryRead(ReadOnlySpan<byte> replay)
    {
        if (replay.Length < 0x10) return null;
        var magic = BinaryPrimitives.ReadUInt32LittleEndian(replay);
        if (magic is 0x50523654 or 0x50523754) return null;
        var offset = BinaryPrimitives.ReadInt32LittleEndian(replay.Slice(0x0c, 4));
        if (offset < 0x10 || offset > replay.Length) return null;
        var blocks = new List<ReplayUserBlock>();
        var cursor = offset;
        while (cursor <= replay.Length - 12)
        {
            var length = BinaryPrimitives.ReadInt32LittleEndian(replay.Slice(cursor + 4, 4));
            if (length < 12 || cursor > replay.Length - length) break;
            var marker = Encoding.ASCII.GetString(replay.Slice(cursor, 4));
            var id = BinaryPrimitives.ReadInt32LittleEndian(replay.Slice(cursor + 8, 4));
            blocks.Add(new ReplayUserBlock(marker, id, replay.Slice(cursor + 12, length - 12).ToArray()));
            cursor += length;
        }
        return new ReplayUserData(offset, blocks.AsReadOnly());
    }

    private static void WriteBlock(Stream stream, ReplayUserBlock block)
    {
        Span<byte> header = stackalloc byte[12];
        Encoding.ASCII.GetBytes(block.Marker.AsSpan(0, Math.Min(4, block.Marker.Length)), header);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..8], block.EncodedLength);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..12], block.Id);
        stream.Write(header);
        stream.Write(block.Data);
    }
}

public static class ReplayTextEncoding
{
    static ReplayTextEncoding() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    public static Encoding ShiftJis => Encoding.GetEncoding(932);
    public static Encoding Utf8 => new UTF8Encoding(false, true);
    public static Encoding FromCodePage(int codePage) => Encoding.GetEncoding(codePage);
}
