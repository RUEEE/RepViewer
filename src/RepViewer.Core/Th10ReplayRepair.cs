using System.Buffers.Binary;

namespace RepViewer.Core;

public static class Th10ReplayRepair
{
    public static bool CanRepair(ReplayDocument replay) => replay.Identity.FormatId == "TH10" &&
        replay.Issues.Any(issue => issue.Code == "stage-frame-count-recovered");

    public static byte[] Repair(ReplayDocument replay)
    {
        if (!CanRepair(replay)) throw new InvalidOperationException("No recoverable TH10 frame-count error.");
        var original = replay.OriginalBytes.ToArray();
        var format = new Th10Format();
        var expected = format.DecodeForAnalysis(original);
        foreach (var stage in replay.Stages)
        {
            var offset = stage.DecodedOffset ?? throw new InvalidDataException("Missing stage offset.");
            BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(offset + 4, 4), checked((uint)stage.Keys.Count));
        }
        var packed = Compress(expected);
        EncodeBlock(packed, 0x80, 0x3d, 0x7a);
        EncodeBlock(packed, 0x400, 0xaa, 0xe1);
        var oldEnd = checked(0x24 + (int)ReplayDecoder.U32(original, 0x1c));
        var result = new byte[checked(0x24 + packed.Length + original.Length - oldEnd)];
        original.AsSpan(0, 0x24).CopyTo(result);
        packed.CopyTo(result, 0x24);
        original.AsSpan(oldEnd).CopyTo(result.AsSpan(0x24 + packed.Length));
        var userOffset = ReplayDecoder.U32(original, 0x0c);
        if (userOffset >= oldEnd && userOffset <= original.Length)
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(0x0c, 4), checked((uint)(userOffset - oldEnd + 0x24 + packed.Length)));
        else if (userOffset != 0) throw new InvalidDataException("Unsupported USER data offset.");
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(0x1c, 4), checked((uint)packed.Length));

        if (!format.DecodeForAnalysis(result).AsSpan().SequenceEqual(expected))
            throw new InvalidDataException("Repaired replay failed binary verification.");
        var verified = ReplayApi.Read(result);
        if (CanRepair(verified) || verified.Stages.Count != replay.Stages.Count ||
            !verified.Stages.Zip(replay.Stages).All(pair => pair.First.StageId == pair.Second.StageId &&
                pair.First.Keys.SequenceEqual(pair.Second.Keys) && pair.First.RawKeys.SequenceEqual(pair.Second.RawKeys) &&
                (pair.First.RawFps ?? []).SequenceEqual(pair.Second.RawFps ?? [])))
            throw new InvalidDataException("Repaired replay failed frame verification.");
        return result;
    }

    private static byte[] Compress(byte[] data)
    {
        const int windowSize = 0x2000;
        const int maximumMatch = 18;
        var packed = new byte[checked((data.Length * 9 + 18 + 7) / 8)];
        var heads = new int[65536];
        var previous = new int[data.Length];
        Array.Fill(heads, -1);
        Array.Fill(previous, -1);
        var bit = 0;
        void Write(int value, int count)
        {
            for (var shift = count - 1; shift >= 0; shift--, bit++)
                if ((value & (1 << shift)) != 0) packed[bit / 8] |= (byte)(0x80 >> (bit % 8));
        }
        int Hash(int position) => ((data[position] * 251 + data[position + 1]) * 251 + data[position + 2]) & 0xffff;
        for (var position = 0; position < data.Length;)
        {
            var bestLength = 0;
            var bestPosition = 0;
            var limit = Math.Min(maximumMatch, data.Length - position);
            if (limit >= 3)
            {
                var candidate = heads[Hash(position)];
                var earliest = Math.Max(0, position - windowSize);
                for (var attempts = 0; candidate >= earliest && attempts < 512; candidate = previous[candidate], attempts++)
                {
                    // The 13-bit index stores ring position + 1; zero is EOF,
                    // so ring position 8191 cannot start a dictionary reference.
                    if ((candidate & (windowSize - 1)) == windowSize - 1) continue;
                    var length = 0;
                    // Reading from the source also handles overlapping matches.
                    while (length < limit && data[candidate + length] == data[position + length]) length++;
                    if (length <= bestLength) continue;
                    bestLength = length;
                    bestPosition = candidate;
                    if (length == limit) break;
                }
            }
            var consumed = bestLength >= 3 ? bestLength : 1;
            if (bestLength >= 3)
            {
                Write(0, 1);
                Write((bestPosition & (windowSize - 1)) + 1, 13);
                Write(bestLength - 3, 4);
            }
            else { Write(1, 1); Write(data[position], 8); }
            for (var end = position + consumed; position < end; position++)
            {
                if (position + 2 >= data.Length) continue;
                var hash = Hash(position);
                previous[position] = heads[hash];
                heads[hash] = position;
            }
        }
        Write(0, 18); // zero dictionary index terminates the stream
        return packed.AsSpan(0, (bit + 7) / 8).ToArray();
    }

    private static void EncodeBlock(byte[] data, int blockSize, byte key, byte add)
    {
        var source = data.ToArray();
        var cursor = 0;
        var left = data.Length;
        if (left % blockSize < blockSize / 4) left -= left % blockSize;
        left -= data.Length & 1;
        while (left > 0)
        {
            var size = Math.Min(left, blockSize);
            var start = cursor;
            for (var position = start + size - 1; position >= start; position -= 2)
            { data[cursor++] = (byte)(source[position] ^ key); key = unchecked((byte)(key + add)); }
            for (var position = start + size - 2; position >= start; position -= 2)
            { data[cursor++] = (byte)(source[position] ^ key); key = unchecked((byte)(key + add)); }
            left -= size;
        }
    }
}
