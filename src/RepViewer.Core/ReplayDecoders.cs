using System.Buffers.Binary;

namespace RepViewer.Core;

internal static class ReplayDecoder
{
    public static void Require(ReadOnlySpan<byte> data, int required)
    {
        if (required < 0 || required > data.Length) throw new InvalidDataException("Replay data ended unexpectedly.");
    }

    public static byte[] Decompress(ReadOnlySpan<byte> packed, int length)
    {
        var output = new byte[length];
        var dict = new byte[0x2010];
        var pointer = 0;
        byte filter = 0x80;
        var dest = 0;
        while (pointer < packed.Length && dest < output.Length)
        {
            if (GetBits(packed, ref pointer, ref filter, 1) != 0)
            {
                if (pointer >= packed.Length) break;
                var value = (byte)GetBits(packed, ref pointer, ref filter, 8);
                output[dest] = dict[dest & 0x1fff] = value;
                dest++;
            }
            else
            {
                if (pointer >= packed.Length) break;
                var index = GetBits(packed, ref pointer, ref filter, 13);
                if (index == 0) break;
                index--;
                if (pointer >= packed.Length) break;
                var count = GetBits(packed, ref pointer, ref filter, 4) + 3;
                for (var i = 0; i < count && dest < output.Length; i++, dest++)
                    output[dest] = dict[dest & 0x1fff] = dict[(index + i) & 0x1fff];
            }
        }
        return output;
    }

    private static int GetBits(ReadOnlySpan<byte> input, ref int pointer, ref byte filter, int count)
    {
        var result = 0;
        var current = pointer < input.Length ? input[pointer] : (byte)0;
        for (var i = 0; i < count; i++)
        {
            result <<= 1;
            if ((current & filter) != 0) result |= 1;
            filter >>= 1;
            if (filter == 0)
            {
                pointer++;
                current = pointer < input.Length ? input[pointer] : (byte)0;
                filter = 0x80;
            }
        }
        return result;
    }

    public static ushort U16(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
    public static uint U24(ReadOnlySpan<byte> data, int offset) => (uint)(data[offset] | data[offset + 1] << 8 | data[offset + 2] << 16);
    public static uint U32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
}

internal sealed record ReplayDecodedData(byte[] Decoded, byte[]? Envelope = null);

internal interface IReplayDecoder
{
    ReplayDecodedData Decode(ReadOnlySpan<byte> buffer);
}

internal sealed class Th06ReplayDecoder : IReplayDecoder
{
    public ReplayDecodedData Decode(ReadOnlySpan<byte> buffer)
    {
        ReplayDecoder.Require(buffer, 0x50);
        var data = buffer.ToArray();
        var key = data[0x0e];
        for (var index = 0x0f; index < data.Length; index++, key = unchecked((byte)(key + 7)))
            data[index] = unchecked((byte)(data[index] - key));
        return new ReplayDecodedData(data, data);
    }
}

internal sealed class OldEncryptedReplayDecoder(
    int requiredLength,
    int packedEndOffset,
    int outputLengthOffset,
    int payloadOffset,
    int keyOffset,
    int decryptStartOffset,
    bool packedValueIsPayloadLength = false,
    bool lengthFieldsEncrypted = false) : IReplayDecoder
{
    public ReplayDecodedData Decode(ReadOnlySpan<byte> buffer)
    {
        var envelope = DecodeEnvelope(buffer);
        return new ReplayDecodedData(DecodeData(envelope), envelope);
    }

    private byte[] DecodeEnvelope(ReadOnlySpan<byte> buffer)
    {
        ReplayDecoder.Require(buffer, requiredLength);
        var data = buffer.ToArray();
        var key = data[keyOffset];
        var index = decryptStartOffset;
        if (lengthFieldsEncrypted)
            DecryptRange(data, ref index, payloadOffset, ref key);

        var packedValue = checked((int)ReplayDecoder.U32(data, packedEndOffset));
        var decryptEnd = packedValueIsPayloadLength ? payloadOffset + packedValue : packedValue;
        DecryptRange(data, ref index, Math.Min(decryptEnd, data.Length), ref key);
        return data;
    }

    private static void DecryptRange(byte[] data, ref int index, int end, ref byte key)
    {
        for (; index < end; index++, key = unchecked((byte)(key + 7)))
            data[index] = unchecked((byte)(data[index] - key));
    }

    private byte[] DecodeData(byte[] envelope)
    {
        var packedValue = checked((int)ReplayDecoder.U32(envelope, packedEndOffset));
        var outputLength = checked((int)ReplayDecoder.U32(envelope, outputLengthOffset));
        var packedLength = packedValueIsPayloadLength
            ? Math.Min(packedValue, envelope.Length - payloadOffset)
            : Math.Min(packedValue, envelope.Length) - payloadOffset;
        return ReplayDecoder.Decompress(envelope.AsSpan(payloadOffset, Math.Max(0, packedLength)), outputLength);
    }
}

internal sealed class ModernReplayDecoder(
    int dataOffset = 0x24,
    int packedLengthOffset = 0x1c,
    int unpackedLengthOffset = 0x20,
    int block1 = 0x400,
    byte base1 = 0x5c,
    byte add1 = 0xe1,
    int block2 = 0x100,
    byte base2 = 0x7d,
    byte add2 = 0x3a) : IReplayDecoder
{
    public ReplayDecodedData Decode(ReadOnlySpan<byte> buffer)
    {
        var packedLength = checked((int)ReplayDecoder.U32(buffer, packedLengthOffset));
        var unpackedLength = checked((int)ReplayDecoder.U32(buffer, unpackedLengthOffset));
        if (packedLength < 0 || unpackedLength <= 0 || dataOffset + packedLength > buffer.Length)
            throw new InvalidDataException("Replay header contains invalid packed sizes.");
        var packed = buffer.Slice(dataOffset, packedLength).ToArray();
        DecodeBlock(packed, block1, base1, add1);
        DecodeBlock(packed, block2, base2, add2);
        return new ReplayDecodedData(ReplayDecoder.Decompress(packed, unpackedLength));
    }

    private static void DecodeBlock(Span<byte> buffer, int blockSize, byte baseValue, byte add)
    {
        var source = buffer.ToArray();
        var p = 0;
        var left = buffer.Length;
        if (left % blockSize < blockSize / 4) left -= left % blockSize;
        left -= buffer.Length & 1;
        while (left > 0)
        {
            if (left < blockSize) blockSize = left;
            var odd = p + blockSize - 1;
            var even = p + blockSize - 2;
            for (var i = 0; i < (blockSize + (blockSize & 1)) / 2; i++, p++, odd -= 2)
            {
                buffer[odd] = (byte)(source[p] ^ baseValue);
                baseValue += add;
            }
            for (var i = 0; i < blockSize / 2; i++, p++, even -= 2)
            {
                buffer[even] = (byte)(source[p] ^ baseValue);
                baseValue += add;
            }
            left -= blockSize;
        }
    }
}

