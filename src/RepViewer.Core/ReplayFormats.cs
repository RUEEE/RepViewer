using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Text;

namespace RepViewer.Core;

internal interface IReplayFormat
{
    string Version { get; }
    ReplayDocument Parse(ReadOnlySpan<byte> buffer);
    byte[] DecodeForAnalysis(ReadOnlySpan<byte> buffer);
}

internal abstract class ReplayFormat : IReplayFormat
{
    public abstract string Version { get; }
    protected abstract IReplayDecoder Decoder { get; }
    public abstract ReplayDocument Parse(ReadOnlySpan<byte> buffer);
    public byte[] DecodeForAnalysis(ReadOnlySpan<byte> buffer) => Decoder.Decode(buffer).Decoded;
}

internal abstract class ReplayFormat<THeader, TStage> : ReplayFormat where THeader : unmanaged where TStage : unmanaged
{
    protected virtual THeader ReadHeader(byte[] decoded) => StructMarshal.Read<THeader>(decoded);
    protected virtual TStage ReadStageHeader(ReadOnlySpan<byte> decoded) => StructMarshal.Read<TStage>(decoded);
    protected ReplayDocument CreatePropertySet(THeader header, List<ReplayStage> stages, ReadOnlySpan<byte> original) =>
        ReplayDocument.Create(Version, header, stages, original);
}

internal sealed record ReplayKeyFrames(List<short> RawKeys, List<ReplayKey> NormalizedKeys, int BytesRead);

internal static class ReplayFormats
{
    public static IReadOnlyList<IReplayFormat> ForMagic(uint magic) => magic switch
    {
        0x50523654 => [new Th06Format()],
        0x50523754 => [new Th07Format()],
        0x50523854 => [new Th08Format()],
        0x50523954 => [new Th09Format()],
        0x72353974 => [new Th095Format()],
        0x72303174 => [new Th10Format()],
        0x72313174 => [new Th11Format()],
        0x72323174 => [new Th12Format()],
        0x35323174 => [new Th125Format()],
        0x72383231 => [new Th128Format()],
        0x72333174 => [new Th13Format(), new Th14Format()],
        0x33343174 => [new Th143Format()],
        0x72353174 => [new Th15Format()],
        0x72363174 => [new Th16Format()],
        0x36353174 => [new Th165Format()],
        0x72373174 => [new Th17Format()],
        0x74373174 => [new Th17TrialFormat()],
        0x72383174 => [new Th18Format()],
        0x74383174 => [new Th18TrialFormat()],
        0x72303274 => [new Th20Format()],
        0x72316c61 => [new AlcostgFormat()],
        _ => []
    };
}

internal static class StructMarshal
{
    public static T Read<T>(ReadOnlySpan<byte> data) where T : unmanaged
    {
        if (data.Length < Unsafe.SizeOf<T>()) throw new InvalidDataException("Replay data ended inside a struct.");
        return MemoryMarshal.Read<T>(data);
    }

    public static byte[] ToBytes<T>(T value) where T : unmanaged
    {
        var bytes = new byte[Unsafe.SizeOf<T>()];
        MemoryMarshal.Write(bytes, in value);
        return bytes;
    }

    public static UnknownField Unknown<T>(T value) where T : unmanaged
    {
        var data = ToBytes(value);
        var result = new UnknownField { Data = data };
        var number = 1;
        foreach (var field in typeof(T).GetFields(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!StructAccess.IsHiddenUnknownField(field.Name))
                continue;
            var offset = Marshal.OffsetOf<T>(field.Name).ToInt32();
            var fixedBuffer = field.GetCustomAttribute<FixedBufferAttribute>();
            var elementType = fixedBuffer?.ElementType ?? field.FieldType;
            var elementSize = Marshal.SizeOf(elementType);
            var length = (fixedBuffer?.Length ?? 1) * elementSize;
            var step = elementSize >= 4 ? 4 : 1;
            var kind = elementSize >= 4 ? "DWORD" : "BYTE";
            for (var part = 0; part < length; part += step)
            {
                var bytes = data.AsSpan(offset + part, Math.Min(step, length - part)).ToArray();
                result.UnknownValues.Add(new UnknownValue($"unknown{number++}", offset + part, bytes, kind));
            }
        }
        foreach (var field in typeof(T).GetFields(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!StructAccess.IsHiddenKnownField(field.Name)) continue;
            var offset = Marshal.OffsetOf<T>(field.Name).ToInt32();
            var fixedBuffer = field.GetCustomAttribute<FixedBufferAttribute>();
            object? display;
            object? raw;
            if (fixedBuffer is null)
            {
                raw = field.GetValue(value);
                display = raw;
            }
            else
            {
                var elementSize = Marshal.SizeOf(fixedBuffer.ElementType);
                var length = fixedBuffer.Length * elementSize;
                var bytes = data.AsSpan(offset, length).ToArray();
                raw = bytes;
                display = StructValueFormatter.FormatFixedBuffer(bytes, fixedBuffer.ElementType);
            }
            result.KnownValues[StructAccess.VisibleFieldName(field.Name)] = new SemanticField(display, raw, field.Name, offset);
        }
        return result;
    }
}

internal static class StructAccess
{
    public static object? Get<T>(T value, string name) where T : unmanaged => typeof(T).GetField(name)?.GetValue(value);
    public static uint UInt32<T>(T value, string name, uint fallback = 0) where T : unmanaged => Get(value, name) is { } raw ? Convert.ToUInt32(raw, CultureInfo.InvariantCulture) : fallback;
    public static byte? Byte<T>(T value, string name) where T : unmanaged
    {
        if (Get(value, name) is not { } raw) return null;
        var number = Convert.ToUInt64(raw, CultureInfo.InvariantCulture);
        return number <= byte.MaxValue ? (byte)number : null;
    }
    public static string Text<T>(T value, string name) where T : unmanaged
    {
        var field = typeof(T).GetField(name) ?? throw new MissingFieldException(typeof(T).Name, name);
        var length = field.GetCustomAttribute<FixedBufferAttribute>()?.Length ?? throw new InvalidOperationException($"{typeof(T).Name}.{name} is not a fixed buffer.");
        var data = StructMarshal.ToBytes(value).AsSpan(Marshal.OffsetOf<T>(name).ToInt32(), length);
        var end = data.IndexOf((byte)0);
        return Encoding.UTF8.GetString(end < 0 ? data : data[..end]).Trim();
    }
    public static bool IsHiddenUnknownField(string name) => name.StartsWith("_unk_", StringComparison.Ordinal);
    public static bool IsHiddenKnownField(string name) =>
        (name.StartsWith('_') && !IsHiddenUnknownField(name)) || name is "PackedLength" or "Magic";
    public static string VisibleFieldName(string name) => IsHiddenUnknownField(name) ? name[5..] : name.StartsWith('_') ? name[1..] : name;
}

internal static class ReplayLayoutFields
{
    public static int StageCount<T>(T header) where T : unmanaged =>
        StructAccess.Get(header, "StageCount") is { } count ? Convert.ToInt32(count, CultureInfo.InvariantCulture) : 1;

    public static ulong? UInt64Field(object value, string name)
    {
        var raw = value.GetType().GetField(name)?.GetValue(value);
        return raw is null ? null : Convert.ToUInt64(raw, CultureInfo.InvariantCulture);
    }

    public static int StageId<T>(T header, int sequence) where T : unmanaged =>
        StructAccess.Get(header, "StageNumber") is { } stage ? Convert.ToInt32(stage, CultureInfo.InvariantCulture) : sequence;

    public static int FrameCount<T>(T header) where T : unmanaged =>
        StructAccess.Get(header, "FrameCount") is { } frames ? Convert.ToInt32(frames, CultureInfo.InvariantCulture) : 0;

    public static int PackedLength<T>(T header) where T : unmanaged =>
        StructAccess.Get(header, "PackedLength") is { } length ? Convert.ToInt32(length, CultureInfo.InvariantCulture) : 0;
}

// Each struct below is a literal decoded replay layout. Reading is equivalent to:
// fread(&header, sizeof(header), 1, file).
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct Th06Header
{
    public fixed byte Magic[4];                   // +0x00
    public ushort Version;                        // +0x04
    public byte Character;                        // +0x06
    public byte Rank;                             // +0x07
    public int _Checksum;                         // +0x08
    public byte _RngValue1;                       // +0x0c
    public byte _RngValue2;                       // +0x0d
    public sbyte _Key;                            // +0x0e
    public sbyte _RngValue3;                      // +0x0f
    public fixed byte Date[9];                    // +0x10
    public fixed byte Name[8];                    // +0x19
    public fixed byte _Padding21[3];              // +0x21
    public int Score;                             // +0x24
    public float _SlowRate2;                      // +0x28
    public float SlowRate;                        // +0x2c
    public float _SlowRate3;                      // +0x30
    public fixed uint _StageOffsets[7];           // +0x34
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct Th06StageHeader
{
    public int Score;                             // +0x00
    public short Rng;                             // +0x04
    public short PointItems;                      // +0x06
    public byte Power;                            // +0x08
    public sbyte Lives;                           // +0x09
    public sbyte Bombs;                           // +0x0a
    public byte StageRank;                        // +0x0b
    public sbyte PowerItemCountForScore;          // +0x0c
    public fixed sbyte _Padding[3];               // +0x0d
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct Th07Header
{
    public fixed byte _unk_Unknown00[2];          // +0x00
    public byte Character;                        // +0x02
    public byte Rank;                             // +0x03
    public fixed byte Date[5];                    // +0x04
    public byte _unk_Unknown09;                   // +0x09
    public fixed byte Name[8];                    // +0x0a
    public byte _unk_Unknown12;                   // +0x12
    public fixed byte _unk_Unknown13[1];          // +0x13
    public uint _unk_Unknown13_d1;                // +0x14
    public uint Score;                            // +0x18
    public fixed uint _unk_Unknown1c[0x17];       // +0x1c
    public float SlowRate;                        // +0x78
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct Th07StageHeader
{
    public uint Score;                            // +0x00
    public uint PointItems;                       // +0x04
    public uint Cherry;                           // +0x08
    public uint CherryMax;                        // +0x0c
    public uint CherryPlus;                       // +0x10
    public uint Graze;                            // +0x14
    public uint _unk_Unknown18;                   // +0x18
    public uint PointExtent;                      // +0x1c (thhylR)
    public fixed byte _unk_Unknown18_b1[2];       // +0x20
    public byte Power;                            // +0x22
    public byte Lives;                            // +0x23
    public byte Bombs;                            // +0x24
    public byte StageRank;                        // +0x25 (thhylR)
    public byte _unk_Unknown26;                   // +0x26
    public byte SpellCount;                       // +0x27 (thhylR)
    public uint _unk_Unknown25_d1;                // +0x28
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct Th08Header
{
    public fixed byte _unk_Unknown00[2];          // +0x00
    public byte Character;                        // +0x02
    public byte Rank;                             // +0x03
    public fixed byte Date[5];                    // +0x04
    public byte _unk_Unknown09;                   // +0x09
    public fixed byte Name[8];                    // +0x0a
    public fixed byte _unk_Unknown12[2];          // +0x12
    public short SpellNumber;                     // +0x14 (thhylR)
    public fixed byte _unk_Unknown16[0x32];       // +0x16
    public int Score;                             // +0x48 (stored in tens)
    public fixed byte _unk_Unknown4c[0x26];       // +0x4c
    public byte ShotSlow;                         // +0x72 (boolean flag)
    public fixed byte _unk_Unknown73[0x3d];       // +0x73
    public float SlowRate;                        // +0xb0
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct Th08StageHeader
{
    public uint Score;                            // +0x00
    public uint PointItems;                       // +0x04
    public uint Graze;                            // +0x08
    public uint Time;                             // +0x0c
    public uint PointExtent;                      // +0x10 (thhylR)
    public uint Piv;                              // +0x14
    public short YoukaiRate;                      // +0x18 (hundredths of a percent)
    public ushort _unk_Unknown1a;                 // +0x1a
    public byte Power;                            // +0x1c
    public byte Lives;                            // +0x1d
    public byte Bombs;                            // +0x1e
    public byte StageRank;                        // +0x1f
    public byte _unk_Unknown20;                   // +0x20
    public byte SpellCount;                       // +0x21
    public byte StageTimePass;                    // +0x22
    public byte _unk_Unknown23;                   // +0x23
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct Th09Header
{
    public uint _unk_Unknown00;                   // +0x00
    public fixed byte Date[8];                    // +0x04
    public fixed byte _unk_Unknown0c[2];          // +0x0c
    public fixed byte Name[9];                    // +0x0e
    public byte Rank;                             // +0x17
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct Th09StageHeader
{
    public uint Score;                            // +0x00
    public ushort Pair;                           // +0x04
    public byte Character;                        // +0x06
    public byte _PlayerType;                      // +0x07 (human/AI role; emitted as P1/P2)
    public byte Lives;                            // +0x08
    public sbyte Place;                           // +0x09
    public fixed byte _unk_Unknown0a[2];          // +0x0a
    public fixed uint _unk_Unknown0c[5];          // +0x0c
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct Th10Header
{
    public fixed byte Name[0xc];                  // +0x00
    public uint Timestamp;                        // +0x0c
    public uint TotalScore;                       // +0x10
    public fixed uint _unk_Unknown14[0xd];        // +0x14
    public float SlowRate;                        // +0x48
    public uint StageCount;                       // +0x4c
    public uint Character;                        // +0x50
    public uint ShotType;                         // +0x54
    public uint Rank;                             // +0x58
    public uint Clear;                            // +0x5c
    public uint _unk_Unknown60;                   // +0x60
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct AlcostgHeader
{
    public fixed byte Name[0x0c];                 // +0x00
    public uint Timestamp;                        // +0x0c
    public uint Score;                            // +0x10
    public fixed uint _unk_Unknown14[0x0c];       // +0x14
    public uint NoDInput;                         // +0x44
    public float SlowRate;                        // +0x48
    public uint StageCount;                       // +0x4c
    public fixed uint _unk_Unknown50[3];          // +0x50
    public uint LastStage;                        // +0x5c
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct AlcostgStage
{
    public ushort StageNumber;                    // +0x00
    public ushort Rng;                            // +0x02
    public uint FrameCount;                       // +0x04
    public uint PackedLength;                     // +0x08 (key bytes + FPS bytes)
    public int Score;                             // +0x0c
    public fixed uint _unk_Unknown10[2];          // +0x10
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct Th11Header
{
    public fixed byte Name[0xc];                  // +0x00
    public uint Timestamp;                        // +0x0c
    public uint _unk_Unknown10;                   // +0x10
    public uint TotalScore;                       // +0x14
    public fixed uint _unk_Unknown18[0xf];        // +0x18
    public float SlowRate;                        // +0x54
    public uint StageCount;                       // +0x58
    public uint Character;                        // +0x5c
    public uint ShotType;                         // +0x60
    public uint Rank;                             // +0x64
    public uint Clear;                            // +0x68
    public uint _unk_Unknown6c;                   // +0x6c
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct Th12Header
{
    public fixed byte Name[0xc];                  // +0x00
    public uint Timestamp;                        // +0x0c
    public uint _unk_Unknown10;                   // +0x10
    public uint TotalScore;                       // +0x14
    public fixed uint _unk_Unknown18[0xf];        // +0x18
    public float SlowRate;                        // +0x54
    public uint StageCount;                       // +0x58
    public uint Character;                        // +0x5c
    public uint ShotType;                         // +0x60
    public uint Rank;                             // +0x64
    public uint Clear;                            // +0x68
    public uint _unk_Unknown6c;                   // +0x6c
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct Th128Header
{
    public fixed byte Name[0xc];                  // +0x00
    public uint Timestamp;                        // +0x0c
    public uint _unk_Unknown10;                   // +0x10
    public uint TotalScore;                       // +0x14
    public fixed uint _unk_Unknown18[0xf];        // +0x18
    public float SlowRate;                        // +0x54
    public uint StageCount;                       // +0x58
    public uint Route;                            // +0x5c
    public uint ShotType;                         // +0x60
    public uint Rank;                             // +0x64
    public uint Clear;                            // +0x68
    public uint _unk_Unknown6c;                   // +0x6c
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct Th13Header
{
    public fixed byte Name[0xc];                  // +0x00
    public uint Timestamp;                        // +0x0c
    public uint _unk_Unknown10;                   // +0x10
    public uint TotalScore;                       // +0x14
    public fixed uint _unk_Unknown18[0xf];        // +0x18
    public float SlowRate;                        // +0x54
    public uint StageCount;                       // +0x58
    public uint Character;                        // +0x5c
    public uint ShotType;                         // +0x60
    public uint Rank;                             // +0x64
    public uint Clear;                            // +0x68
    public fixed uint _unk_Unknown6c[2];          // +0x6c
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct Th14Header
{
    public fixed byte Name[0xc];                  // +0x00
    public uint Timestamp;                        // +0x0c
    public uint _unk_Unknown10;                   // +0x10
    public uint TotalScore;                       // +0x14
    public fixed uint _unk_Unknown18[0x17];       // +0x18
    public float SlowRate;                        // +0x74
    public uint StageCount;                       // +0x78
    public uint Character;                        // +0x7c
    public uint ShotType;                         // +0x80
    public uint Rank;                             // +0x84
    public uint Clear;                            // +0x88
    public fixed uint _unk_Unknown8c[2];          // +0x8c
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct Th143Header
{
    public fixed byte Name[0xc];                  // +0x00
    public fixed uint _unk_Unknown0c[2];          // +0x0c
    public uint Timestamp;                        // +0x14
    public uint _unk_Unknown18;                   // +0x18
    public uint TotalScore;                       // +0x1c
    public fixed uint _unk_Unknown20[0x17];       // +0x20
    public float SlowRate;                        // +0x7c
    public uint _UnusedStageCount;                // +0x80
    public uint _unk_Unknown84;                   // +0x84
    public uint Day;                              // +0x88
    public uint Scene;                            // +0x8c
    public uint Stage;                            // +0x90
    public uint MainItem;                         // +0x94
    public uint SubItem;                          // +0x98
    public uint SubItemCount;                     // +0x9c
    public uint MainItemCount;                    // +0xa0
    public uint MainItemDurationOrRange;           // +0xa4
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct Th15Header
{
    public fixed byte Name[0xc];                  // +0x00
    public uint Timestamp;                        // +0x0c
    public uint _unk_Unknown10;                   // +0x10
    public uint TotalScore;                       // +0x14
    public fixed uint _unk_Unknown18[0x1b];       // +0x18
    public float SlowRate;                        // +0x84
    public uint StageCount;                       // +0x88
    public uint Character;                        // +0x8c
    public uint ShotType;                         // +0x90
    public uint Rank;                             // +0x94
    public uint Clear;                            // +0x98
    public fixed uint _unk_Unknown9c[2];          // +0x9c
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct Th16Header
{
    public fixed byte Name[0xc];                  // +0x00
    public uint Timestamp;                        // +0x0c
    public uint _unk_Unknown10;                   // +0x10
    public uint TotalScore;                       // +0x14
    public fixed uint _unk_Unknown18[0x19];       // +0x18
    public float SlowRate;                        // +0x7c
    public uint StageCount;                       // +0x80
    public uint Character;                        // +0x84
    public uint _unk_Unknown88;                   // +0x88
    public uint Rank;                             // +0x8c
    public uint Clear;                            // +0x90
    public fixed uint _unk_Unknown94[2];          // +0x94
    public uint ShotType;                         // +0x9c
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct Th17Header
{
    public fixed byte Name[0x10];                 // +0x00
    public uint Timestamp;                        // +0x10
    public uint _unk_Unknown14;                   // +0x14
    public uint TotalScore;                       // +0x18
    public fixed uint _unk_Unknown1c[0x19];       // +0x1c
    public float SlowRate;                        // +0x80
    public uint StageCount;                       // +0x84
    public uint Character;                        // +0x88
    public uint ShotType;                         // +0x8c
    public uint Rank;                             // +0x90
    public uint Clear;                            // +0x94
    public fixed uint _unk_Unknown98[2];          // +0x98
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct Th18Header
{
    public fixed byte Name[0x10];                 // +0x00
    public uint Timestamp;                        // +0x10
    public uint _unk_Unknown14;                   // +0x14
    public uint TotalScore;                       // +0x18
    public fixed uint _unk_Unknown1c[0x22];       // +0x1c
    public float SlowRate;                        // +0xa4
    public uint StageCount;                       // +0xa8
    public uint Character;                        // +0xac
    public uint ShotType;                         // +0xb0
    public uint Rank;                             // +0xb4
    public uint Clear;                            // +0xb8
    public fixed uint _unk_Unknownbc[3];          // +0xbc
}

// Trial layouts intentionally have their own structs and formats. They are not aliases:
// fields may diverge independently when another trial revision is encountered.
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct Th17TrialHeader
{
    public fixed byte Name[0x10];
    public uint Timestamp;
    public uint _unk_Unknown14;
    public uint TotalScore;
    public fixed uint _unk_Unknown1c[0x19];
    public float SlowRate;
    public uint StageCount;
    public uint Character;
    public uint ShotType;
    public uint Rank;
    public uint Clear;
    public fixed uint _unk_Unknown98[2];
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct Th18TrialHeader
{
    public fixed byte Name[0x10];
    public uint Timestamp;
    public uint _unk_Unknown14;
    public uint TotalScore;
    public fixed uint _unk_Unknown1c[0x22];
    public float SlowRate;
    public uint StageCount;
    public uint Character;
    public uint ShotType;
    public uint Rank;
    public uint Clear;
    public fixed uint _unk_Unknownbc[3];
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct Th20Header
{
    public fixed byte Name[0xc];                  // +0x00
    public uint _unk_Unknown0c;                   // +0x0c
    public uint Timestamp;                        // +0x10
    public uint _unk_Unknown14;                   // +0x14
    public uint TotalScore;                       // +0x18
    public fixed uint _unk_Unknown1c[0x2d];       // +0x1c
    public float SlowRate;                        // +0xd0
    public uint StageCount;                       // +0xd4
    public uint Character;                        // +0xd8
    public uint Equip0Type;                       // +0xdc
    public uint Equip1Type;                       // +0xe0
    public uint Equip2Type;                       // +0xe4
    public uint Equip3Type;                       // +0xe8
    public uint _unk_UnknownEc;                   // +0xec
    public uint Rank;                             // +0xf0
    public uint Clear;                            // +0xf4
    public uint _unk_UnknownF8;                   // +0xf8
    public int SpellNumber;                       // +0xfc
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct Th95Header
{
    public ushort StageNo;                        // +0x00 (thhylR: spell/stage identifier)
    public byte LevelId;                          // +0x02
    public byte SubLevelId;                       // +0x03
    public fixed byte _unk_Unknown04[3];          // +0x04
    public fixed byte Name[8];                    // +0x07
    public byte _unk_Unknown0f;                   // +0x0f
    public uint Timestamp;                        // +0x10
    public uint TotalScore;                       // +0x14
    public fixed uint _unk_Unknown18[0x32];       // +0x18
    public float SlowRate;                        // +0xe0
    public uint _unk_UnknownE4;                   // +0xe4
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct Th125Header
{
    public fixed byte Name[8];                    // +0x00
    public uint _unk_Unknown08;                   // +0x08
    public uint Timestamp;                        // +0x0c
    public uint _unk_Unknown10;                   // +0x10
    public uint TotalScore;                       // +0x14
    public fixed uint _unk_Unknown18[0xf];        // +0x18
    public float SlowRate;                        // +0x54
    public uint Character;                        // +0x58
    public uint _unk_Unknown5c;                   // +0x5c
    public uint LevelId;                          // +0x60
    public uint SubLevelId;                       // +0x64
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct Th165Header
{
    public fixed byte Name[0xc];                  // +0x00
    public fixed uint _unk_Unknown0c[2];          // +0x0c
    public uint Timestamp;                        // +0x14
    public uint _unk_Unknown18;                   // +0x18
    public uint TotalScore;                       // +0x1c
    public fixed uint _unk_Unknown20[0x1b];       // +0x20
    public uint Day;                              // +0x8c
    public uint Scene;                            // +0x90
    public uint Stage;                            // +0x94
    public uint PowerLevel;                       // +0x98
    public uint Retried;                          // +0x9c
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct Th10Stage
{
    public ushort StageNumber;                    // +0x00
    public ushort Rng;                            // +0x02
    public uint FrameCount;                       // +0x04
    public uint PackedLength;                     // +0x08
    public uint Score;                            // +0x0c
    public uint Power;                            // +0x10
    public uint Faith;                            // +0x14
    public uint FaithGauge;                       // +0x18
    public uint Lives;                            // +0x1c
    public uint _unk_Unknown20;                   // +0x20
    public int XPositionRaw;                      // +0x24
    public int YPositionRaw;                      // +0x28
    public fixed uint _unk_Unknown2c[0x66];       // +0x2c
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct Th11Stage
{
    public ushort StageNumber;                    // +0x00
    public ushort Rng;                            // +0x02
    public uint FrameCount;                       // +0x04
    public uint PackedLength;                     // +0x08
    public uint Score;                            // +0x0c
    public uint Power;                            // +0x10
    public uint Piv;                              // +0x14
    public ushort Lives;                          // +0x18
    public ushort LifePieces;                     // +0x1a
    public fixed uint _unk_Unknown1c[6];          // +0x1c
    public uint Graze;                            // +0x34
    public fixed uint _unk_Unknown38[0x16];       // +0x38
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct Th12Stage
{
    public ushort StageNumber;                    // +0x00
    public ushort Rng;                            // +0x02
    public uint FrameCount;                       // +0x04
    public uint PackedLength;                     // +0x08
    public uint Score;                            // +0x0c
    public uint Power;                            // +0x10
    public uint Piv;                              // +0x14
    public ushort Lives;                          // +0x18
    public ushort LifePieces;                     // +0x1a
    public ushort Bombs;                          // +0x1c
    public ushort BombPieces;                     // +0x1e
    public uint Ufo1;                             // +0x20
    public uint Ufo2;                             // +0x24
    public uint Ufo3;                             // +0x28
    public fixed uint _unk_Unknown2c[6];          // +0x2c
    public uint Graze;                            // +0x44
    public fixed uint _unk_Unknown48[0x16];       // +0x48
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct Th128Stage
{
    public ushort StageNumber;                    // +0x00
    public ushort Rng;                            // +0x02
    public uint FrameCount;                       // +0x04
    public uint PackedLength;                     // +0x08
    public uint Score;                            // +0x0c
    public uint FreezeLevel;                      // +0x10
    public uint FreezePower;                      // +0x14
    public int XPositionRaw;                      // +0x18
    public int YPositionRaw;                      // +0x1c
    public uint Continues;                        // +0x20
    public uint _unk_Unknown24;                   // +0x24
    public uint Graze;                            // +0x28
    public uint _unk_Unknown2c;                   // +0x2c
    public fixed int _SpellTimesRaw[20];          // +0x30
    public uint Motivation;                       // +0x80
    public uint PerfectFreeze;                    // +0x84
    public float FreezeArea;                      // +0x88
    public uint _unk_Unknown8c;                   // +0x8c
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct Th13Stage
{
    public ushort StageNumber;                    // +0x00
    public ushort Rng;                            // +0x02
    public uint FrameCount;                       // +0x04
    public uint PackedLength;                     // +0x08
    public int XPositionRaw;                      // +0x0c
    public int YPositionRaw;                      // +0x10
    public fixed uint _unk_Unknown14[2];          // +0x14
    public uint Score;                            // +0x1c
    public fixed uint _unk_Unknown20[3];          // +0x20
    public uint Graze;                            // +0x2c
    public fixed uint _unk_Unknown30[2];          // +0x30
    public uint Piv;                              // +0x38
    public fixed uint _unk_Unknown3c[2];          // +0x3c
    public uint Power;                            // +0x44
    public fixed uint _unk_Unknown48[2];          // +0x48
    public uint Lives;                            // +0x50
    public uint LifePieces;                       // +0x54
    public uint ExtentCount;                      // +0x58
    public uint Bombs;                            // +0x5c
    public uint BombPieces;                       // +0x60
    public uint Trance;                           // +0x64
    public fixed uint _unk_Unknown68[2];          // +0x68
    public fixed int _SpellTimesRaw[20];          // +0x70
    public uint _unk_Unknownc0;                   // +0xc0
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct Th14Stage
{
    public ushort StageNumber;                    // +0x00
    public ushort Rng;                            // +0x02
    public uint FrameCount;                       // +0x04
    public uint PackedLength;                     // +0x08
    public int XPositionRaw;                      // +0x0c
    public int YPositionRaw;                      // +0x10
    public fixed uint _unk_Unknown14[2];          // +0x14
    public uint Score;                            // +0x1c
    public fixed uint _unk_Unknown20[3];          // +0x20
    public uint Graze;                            // +0x2c
    public fixed uint _unk_Unknown30[2];          // +0x30
    public uint Piv;                              // +0x38
    public fixed uint _unk_Unknown3c[2];          // +0x3c
    public uint Power;                            // +0x44
    public fixed uint _unk_Unknown48[2];          // +0x48
    public uint Lives;                            // +0x50
    public uint LifePieces;                       // +0x54
    public uint ExtentCount;                      // +0x58
    public uint Bombs;                            // +0x5c
    public uint BombPieces;                       // +0x60
    public uint ScoreFromPoc;                     // +0x64
    public fixed uint _unk_Unknown68[6];          // +0x68
    public uint NormalFragmentCount;              // +0x80
    public uint PocCount;                         // +0x84
    public fixed int _SpellTimesRaw[20];          // +0x88
    public uint _unk_Unknownd8;                   // +0xd8
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct Th143Stage
{
    public ushort StageNumber;                    // +0x00
    public ushort Rng;                            // +0x02
    public uint FrameCount;                       // +0x04
    public uint PackedLength;                     // +0x08
    public uint Score;                            // +0x0c
    public fixed uint _unk_Unknown10[0x3f];       // +0x10
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct Th15Stage
{
    public ushort StageNumber;                    // +0x00
    public ushort Rng;                            // +0x02
    public uint FrameCount;                       // +0x04
    public uint PackedLength;                     // +0x08
    public fixed uint _unk_Unknown0c[9];          // +0x0c
    public uint Score;                            // +0x30
    public uint Rank;                             // +0x34
    public uint Continues;                        // +0x38
    public uint _unk_Unknown3c;                   // +0x3c
    public uint Graze;                            // +0x40
    public uint _GrazeChapter;                    // +0x44
    public uint _SpellPracticeId;                 // +0x48
    public uint _unk_Unknown4c;                   // +0x4c
    public uint MissCount;                        // +0x50
    public uint PointItems;                       // +0x54
    public uint Piv;                              // +0x58
    public uint _PivMin;                          // +0x5c
    public uint _PivMax;                          // +0x60
    public uint Power;                            // +0x64
    public uint _PowerMax;                        // +0x68
    public uint _PowerLevelUp;                    // +0x6c
    public uint _unk_Unknown70;                   // +0x70
    public uint Lives;                            // +0x74
    public uint LifePieces;                       // +0x78
    public uint _Extends;                         // +0x7c
    public uint Bombs;                            // +0x80
    public uint BombPieces;                       // +0x84
    public fixed uint _unk_Unknown88[0x6c];       // +0x88
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct Th16Stage
{
    public ushort StageNumber;                    // +0x00
    public ushort Rng;                            // +0x02
    public uint FrameCount;                       // +0x04
    public uint PackedLength;                     // +0x08
    public fixed uint _unk_Unknown0c[0xa];        // +0x0c
    public uint Score;                            // +0x34
    public uint Rank;                             // +0x38
    public uint Continues;                        // +0x3c
    public uint _unk_Unknown40;                   // +0x40
    public uint Graze;                            // +0x44
    public fixed uint _unk_Unknown48[2];          // +0x48
    public uint MissCount;                        // +0x50
    public uint PointItems;                       // +0x54
    public uint _unk_Unknown58;                   // +0x58
    public uint Piv;                              // +0x5c
    public fixed uint _unk_Unknown60[2];          // +0x60
    public uint Power;                            // +0x68
    public fixed uint _unk_Unknown6c[3];          // +0x6c
    public uint Lives;                            // +0x78
    public uint LifePieces;                       // +0x7c
    public uint _unk_Unknown80;                   // +0x80
    public uint Bombs;                            // +0x84
    public uint BombPieces;                       // +0x88
    public uint SeasonPower;                      // +0x8c
    public uint SeasonPowerMax;                   // +0x90
    public fixed uint _unk_Unknown94[0x80];       // +0x94
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct Th17Stage
{
    public ushort StageNumber;                    // +0x00
    public ushort Rng;                            // +0x02
    public uint FrameCount;                       // +0x04
    public uint PackedLength;                     // +0x08
    public fixed uint _unk_Unknown0c[0xa];        // +0x0c
    public uint Score;                            // +0x34
    public fixed uint _unk_Unknown38[3];          // +0x38
    public uint Graze;                            // +0x44
    public fixed uint _unk_Unknown48[2];          // +0x48
    public uint MissCount;                        // +0x50
    public uint _unk_Unknown54;                   // +0x54
    public uint PointItems;                       // +0x58
    public uint Piv;                              // +0x5c
    public fixed uint _unk_Unknown60[2];          // +0x60
    public uint Power;                            // +0x68
    public fixed uint _unk_Unknown6c[3];          // +0x6c
    public uint Lives;                            // +0x78
    public uint LifePieces;                       // +0x7c
    public uint _unk_Unknown80;                   // +0x80
    public uint Bombs;                            // +0x84
    public uint BombPieces;                       // +0x88
    public fixed uint _unk_Unknown8c[4];          // +0x8c
    public fixed uint Spirits[5];                 // +0x9c
    public fixed uint _unk_UnknownB0[0xc];        // +0xb0
    public uint InitialRoarTime;                  // +0xe0
    public fixed uint _unk_UnknownE4[0x1d];       // +0xe4
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct Th18Stage
{
    public ushort StageNumber;                    // +0x00
    public ushort Rng;                            // +0x02
    public uint FrameCount;                       // +0x04
    public uint PackedLength;                     // +0x08
    public int XPositionRaw;                      // +0x0c
    public int YPositionRaw;                      // +0x10
    public uint _unk_Unknown14;                   // +0x14
    public fixed int _SpellTimesRaw[20];          // +0x18
    public fixed uint _unk_Unknown68[8];          // +0x68
    public uint Score;                            // +0x88
    public fixed uint _unk_Unknown8c[3];          // +0x8c
    public uint Graze;                            // +0x98
    public fixed uint _unk_Unknown9c[9];          // +0x9c
    public uint Piv;                              // +0xc0
    public uint Power;                            // +0xc4
    public fixed uint _unk_Unknownc8[3];          // +0xc8
    public uint Lives;                            // +0xd4
    public uint LifePieces;                       // +0xd8
    public uint ExtentCount;                      // +0xdc
    public uint _unk_UnknownE0;                   // +0xe0
    public uint Bombs;                            // +0xe4
    public uint BombPieces;                       // +0xe8
    public fixed uint _unk_UnknownEc[0x1e];       // +0xec
    public fixed int _CardIds[256];               // +0x164
    public fixed int _CardCooldownFrames[256];    // +0x564
    public fixed uint _unk_Unknown964[0x40];       // +0x964
    public fixed int _CardsAfterShopIds[256];     // +0xa64
    public fixed int _CardsAfterShopCooldownFrames[256]; // +0xe64
    public fixed uint _unk_Unknown1264[2];         // +0x1264
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct Th17TrialStage
{
    public ushort StageNumber;
    public ushort Rng;
    public uint FrameCount;
    public uint PackedLength;
    public fixed uint _unk_Unknown0c[0xa];
    public uint Score;
    public fixed uint _unk_Unknown38[3];
    public uint Graze;
    public fixed uint _unk_Unknown48[2];
    public uint MissCount;
    public uint _unk_Unknown54;
    public uint PointItems;
    public uint Piv;
    public fixed uint _unk_Unknown60[2];
    public uint Power;
    public fixed uint _unk_Unknown6c[3];
    public uint Lives;
    public uint LifePieces;
    public uint _unk_Unknown80;
    public uint Bombs;
    public uint BombPieces;
    public fixed uint _unk_Unknown8c[4];
    public fixed uint Spirits[5];
    public fixed uint _unk_UnknownB0[0xc];
    public uint InitialRoarTime;
    public fixed uint _unk_UnknownE4[0x1d];
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct Th18TrialStage
{
    public ushort StageNumber;                    // +0x00
    public ushort Rng;                            // +0x02
    public uint FrameCount;                       // +0x04
    public uint PackedLength;                     // +0x08
    public int XPositionRaw;                      // +0x0c
    public int YPositionRaw;                      // +0x10
    public fixed uint _unk_Unknown14[8];          // +0x14
    public uint Score;                            // +0x34
    public fixed uint _unk_Unknown38[3];          // +0x38
    public uint Graze;                            // +0x44
    public fixed uint _unk_Unknown48[9];          // +0x48
    public uint Piv;                              // +0x6c
    public uint Power;                            // +0x70
    public fixed uint _unk_Unknown74[3];          // +0x74
    public uint Lives;                            // +0x80
    public uint LifePieces;                       // +0x84
    public uint ExtentCount;                      // +0x88
    public uint _unk_Unknown8c;                   // +0x8c
    public uint Bombs;                            // +0x90
    public uint BombPieces;                       // +0x94
    public fixed uint _unk_Unknown98[0x1f];       // +0x98
    public fixed int _SpellTimesRaw[20];          // +0x114
    public fixed int _CardIds[256];               // +0x164
    public fixed int _CardCooldownFrames[256];    // +0x564
    public fixed uint _unk_Unknown964[2];         // +0x964
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct Th20Stage
{
    public uint StageNumber;                      // +0x00
    public uint _unk_Unknown04;                   // +0x04
    public uint FrameCount;                       // +0x08
    public uint PackedLength;                     // +0x0c
    public int XPositionRaw;                      // +0x10
    public int YPositionRaw;                      // +0x14
    public uint _unk_Unknown18;                   // +0x18
    public fixed int _SpellTimesRaw[20];          // +0x1c
    public uint _unk_Unknown6c;                   // +0x6c
    public uint Score;                            // +0x70
    public fixed uint _unk_Unknown74[0xb];        // +0x74
    public uint Power;                            // +0xa0
    public fixed uint _unk_UnknownA4[4];          // +0xa4
    public uint Piv;                              // +0xb4
    public uint _unk_UnknownB8;                   // +0xb8
    public uint YihenAttackGauge;                 // +0xbc
    public uint YihenAttackGaugeMax;              // +0xc0
    public fixed uint _unk_UnknownC4[2];          // +0xc4
    public uint YihenEnemyGauge;                  // +0xcc
    public uint YihenEnemyGaugeMax;               // +0xd0
    public uint YihenEnemyRed;                    // +0xd4
    public uint YihenEnemyBlue;                   // +0xd8
    public uint YihenEnemyYellow;                 // +0xdc
    public uint YihenEnemyGreen;                  // +0xe0
    public uint YihenEnemyRedLevel;               // +0xe4
    public uint YihenEnemyBlueLevel;              // +0xe8
    public uint YihenEnemyYellowLevel;            // +0xec
    public uint YihenEnemyGreenLevel;             // +0xf0
    public uint YihenEnemyRedCount;               // +0xf4
    public uint YihenEnemyBlueCount;              // +0xf8
    public uint YihenEnemyYellowCount;            // +0xfc
    public uint YihenEnemyGreenCount;              // +0x100
    public fixed uint _unk_Unknown104[9];         // +0x104
    public uint Lives;                            // +0x128
    public uint _unk_Unknown12c;                  // +0x12c
    public uint LifePieces;                       // +0x130
    public uint ExtentCount;                      // +0x134
    public uint _unk_Unknown138;                  // +0x138
    public uint Bombs;                            // +0x13c
    public uint BombPieces;                       // +0x140
    public fixed uint _unk_Unknown144[4];         // +0x144
    public uint Graze;                            // +0x154
    public fixed uint _unk_Unknown158[0x52];      // +0x158
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct Th95Stage
{
    public uint _unk_Unknown00;                   // +0x00
    public uint _UnusedFrameCount;                // +0x04
    public uint PackedLength;                     // +0x08
    public uint _unk_Unknown0c;                   // +0x0c
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct Th125Stage
{
    public ushort StageNumber;                    // +0x00
    public ushort Rng;                            // +0x02
    public uint FrameCount;                       // +0x04
    public uint PackedLength;                     // +0x08
    public fixed uint _unk_Unknown0c[0x25];       // +0x0c
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct Th165Stage
{
    public uint _unk_Unknown00;                   // +0x00
    public uint FrameCount;                       // +0x04
    public uint PackedLength;                     // +0x08
    public fixed uint _unk_Unknown0c[0x35];       // +0x0c
}

internal abstract class OldFormat<THeader, TStage> : ReplayFormat<THeader, TStage> where THeader : unmanaged where TStage : unmanaged
{
    protected static ReplayStage Stage(int stageId, Dictionary<string, object?> fields, List<short> raw, List<ReplayKey> normalized, object? header = null, int? decodedOffset = null) =>
        new() { StageId = stageId, Fields = fields, RawKeys = raw, Keys = normalized, RawHeader = header, DecodedOffset = decodedOffset };

    protected static ReplayKey NormalizeOld(uint payload)
    {
        var key = (ReplayKey)((payload >> 4) & 0xf);
        var action = payload & 0xf;
        if ((action & 1) != 0) key |= ReplayKey.Z;
        if ((action & 2) != 0) key |= ReplayKey.X;
        if ((action & 4) != 0) key |= ReplayKey.Shift;
        if ((payload & 0x100) != 0) key |= ReplayKey.Ctrl;
        return key;
    }

    protected static (List<short> Raw, List<ReplayKey> Normalized) ReadFixedOldKeys(ReadOnlySpan<byte> data, int stageId, int offset, int end, int headerSize, int stride, string version)
    {
        ReplayDecoder.Require(data, offset + headerSize);
        if (end <= offset + headerSize || end > data.Length) throw new InvalidDataException($"{version} stage {stageId} offset is invalid.");
        var frames = (end - offset - headerSize) / stride;
        var raw = new List<short>(frames);
        var normalized = new List<ReplayKey>(frames);
        for (var frame = 0; frame < frames; frame++)
        {
            var value = stride == 4 ? ReplayDecoder.U32(data, offset + headerSize + frame * stride) : ReplayDecoder.U16(data, offset + headerSize + frame * stride);
            raw.Add(unchecked((short)value));
            normalized.Add(NormalizeOld(value));
        }
        return (raw, normalized);
    }

    protected static void AssignTrailingFps(ReadOnlySpan<byte> data, int fpsOffset, IEnumerable<ReplayStage> stages)
    {
        foreach (var stage in stages)
        {
            var sampleCount = DivideRoundUp(stage.Keys.Count, 30);
            ReplayDecoder.Require(data, fpsOffset + sampleCount);
            stage.RawFps = data.Slice(fpsOffset, sampleCount).ToArray().ToList();
            stage.Fps = stage.RawFps.Select(value => (byte)(value & 0x7f)).ToList();
            fpsOffset += sampleCount;
        }
    }

    protected static int DivideRoundUp(int value, int divisor) => (value + divisor - 1) / divisor;
}

internal sealed class Th06Format : OldFormat<Th06Header, Th06StageHeader>
{
    public override string Version => "TH06";
    protected override IReplayDecoder Decoder { get; } = new Th06ReplayDecoder();

    public override ReplayDocument Parse(ReadOnlySpan<byte> buffer)
    {
        var decoded = Decoder.Decode(buffer);
        return ParseDecoded(decoded.Decoded, buffer);
    }

    private ReplayDocument ParseDecoded(byte[] data, ReadOnlySpan<byte> original)
    {
        ReplayDecoder.Require(data, 0x50);
        var header = ReadHeader(data);
        var stages = new List<ReplayStage>();
        for (var index = 0; index < 7; index++)
        {
            var offset = checked((int)ReplayDecoder.U32(data, 0x34 + index * 4));
            if (offset == 0) continue;
            ReplayDecoder.Require(data, offset + 0x18);
            var stageHeader = ReadStageHeader(data.AsSpan(offset));
            var raw = new List<short>();
            var normalized = new List<ReplayKey>();
            for (var p = offset + 0x10; p + 8 <= data.Length;)
            {
                var frame = ReplayDecoder.U32(data, p);
                if (frame == 9_999_999) break;
                var payload = ReplayDecoder.U32(data, p + 4);
                var next = p + 8;
                if (next + 4 > data.Length) break;
                var nextFrame = ReplayDecoder.U32(data, next);
                if (nextFrame == 9_999_999) break;
                for (var i = frame; i < nextFrame && i < 2_000_000; i++)
                {
                    raw.Add(unchecked((short)payload));
                    normalized.Add(NormalizeOld(payload));
                }
                p = next;
            }
            var fields = new Dictionary<string, object?>();
            stages.Add(Stage(index + 1, fields, raw, normalized, stageHeader, offset));
        }
        return CreatePropertySet(header, stages, original);
    }
}

internal sealed class Th07Format : OldFormat<Th07Header, Th07StageHeader>
{
    public override string Version => "TH07";
    protected override IReplayDecoder Decoder { get; } = new OldEncryptedReplayDecoder(
        requiredLength: 0x54,
        packedEndOffset: 0x14,
        outputLengthOffset: 0x18,
        payloadOffset: 0x54,
        keyOffset: 0x0d,
        decryptStartOffset: 0x10,
        packedValueIsPayloadLength: true,
        lengthFieldsEncrypted: true);

    public override ReplayDocument Parse(ReadOnlySpan<byte> buffer)
    {
        var decoded = Decoder.Decode(buffer);
        return ParseDecoded(decoded.Envelope ?? decoded.Decoded, decoded.Decoded, buffer);
    }

    private ReplayDocument ParseDecoded(byte[] encrypted, byte[] data, ReadOnlySpan<byte> original)
    {
        ReplayDecoder.Require(encrypted, 0x54);
        var header = ReadHeader(data);
        var ranges = new List<(int Stage, int Offset, int End)>();
        var extraOffset = ReplayDecoder.U32(encrypted, 0x34);
        if (extraOffset != 0)
        {
            ranges.Add((7, RelativeOffset(extraOffset), RelativeOffset(ReplayDecoder.U32(encrypted, 0x50))));
        }
        else if (ReplayDecoder.U32(encrypted, 0x1c) == 0)
        {
            for (var i = 0; i < 6; i++)
            {
                var absolute = ReplayDecoder.U32(encrypted, 0x1c + i * 4);
                if (absolute == 0) continue;
                ranges.Add((i + 1, RelativeOffset(absolute), RelativeOffset(ReplayDecoder.U32(encrypted, 0x38 + i * 4))));
                break;
            }
        }
        else
        {
            var starts = new List<(int Stage, int Offset)>();
            for (var i = 0; i < 7; i++)
            {
                var absolute = ReplayDecoder.U32(encrypted, 0x1c + i * 4);
                if (absolute == 0) continue;
                starts.Add((i + 1, RelativeOffset(absolute)));
            }
            var end = RelativeOffset(ReplayDecoder.U32(encrypted, 0x38));
            ranges.AddRange(starts.Select((start, index) =>
                (start.Stage, start.Offset, index + 1 < starts.Count ? starts[index + 1].Offset : end)));
        }

        if (ranges.Count == 0) throw new InvalidDataException("TH07 replay contains no stage offsets.");
        var finalEnd = ranges[^1].End;
        var stages = ranges.Select(range => ParseStage(data, range.Stage, range.Offset, range.End)).ToList();
        AssignTrailingFps(data, finalEnd, stages);
        return CreatePropertySet(header, stages, original);

        static int RelativeOffset(uint absolute) => checked((int)absolute - 0x54);
    }

    private static ReplayStage ParseStage(ReadOnlySpan<byte> data, int stageId, int offset, int end)
    {
        const int headerSize = 0x2c;
        const int stride = 4;
        var (raw, normalized) = ReadFixedOldKeys(data, stageId, offset, end, headerSize, stride, "TH07");
        var header = StructMarshal.Read<Th07StageHeader>(data.Slice(offset, headerSize));
        var fields = new Dictionary<string, object?>();
        return Stage(stageId, fields, raw, normalized, header, offset);
    }
}

internal sealed class Th08Format : OldFormat<Th08Header, Th08StageHeader>
{
    public override string Version => "TH08";
    protected override IReplayDecoder Decoder { get; } = new OldEncryptedReplayDecoder(
        requiredLength: 0x68,
        packedEndOffset: 0x0c,
        outputLengthOffset: 0x1c,
        payloadOffset: 0x68,
        keyOffset: 0x15,
        decryptStartOffset: 0x18);

    public override ReplayDocument Parse(ReadOnlySpan<byte> buffer)
    {
        var decoded = Decoder.Decode(buffer);
        return ParseDecoded(decoded.Envelope ?? decoded.Decoded, decoded.Decoded, buffer);
    }

    private ReplayDocument ParseDecoded(byte[] encrypted, byte[] data, ReadOnlySpan<byte> original)
    {
        ReplayDecoder.Require(encrypted, 0x68);
        var header = ReadHeader(data);
        var offsets = new int?[18];
        var lengths = new int[18];
        var lastValid = -1;
        for (var slot = 0; slot < offsets.Length; slot++)
        {
            var absolute = checked((int)ReplayDecoder.U32(encrypted, 0x20 + slot * 4));
            var relative = absolute - 0x68;
            if (relative < 0 || relative >= data.Length) continue;
            if (lastValid >= 0)
            {
                var length = relative - offsets[lastValid]!.Value;
                if (length <= 0) continue;
                lengths[lastValid] = length;
            }
            offsets[slot] = relative;
            lastValid = slot;
        }
        if (lastValid < 0) throw new InvalidDataException("TH08 replay contains no valid stage offsets.");
        lengths[lastValid] = data.Length - offsets[lastValid]!.Value;

        var stages = new List<ReplayStage>();
        for (var slot = 0; slot < 9; slot++)
        {
            if (offsets[slot] is not { } offset) continue;
            var end = offset + lengths[slot];
            ReplayStage stage;
            try { stage = ParseStage(data, slot + 1, offset, end); }
            catch (Exception exception)
            {
                throw new InvalidDataException($"TH08 stage slot {slot + 1} failed (offset=0x{offset:x}, end=0x{end:x}, decoded=0x{data.Length:x}).", exception);
            }
            var fpsSlot = slot + 9;
            if (offsets[fpsSlot] is { } fpsOffset)
            {
                var expected = DivideRoundUp(stage.Keys.Count, 30);
                var available = Math.Min(expected, lengths[fpsSlot]);
                stage.RawFps = data.AsSpan(fpsOffset, available).ToArray().ToList();
                stage.Fps = stage.RawFps.Select(value => (byte)(value & 0x7f)).ToList();
            }
            stages.Add(stage);
        }
        return CreatePropertySet(header, stages, original);
    }

    private static ReplayStage ParseStage(ReadOnlySpan<byte> data, int stageId, int offset, int end)
    {
        const int headerSize = 0x24;
        const int stride = 2;
        var (raw, normalized) = ReadFixedOldKeys(data, stageId, offset, end, headerSize, stride, "TH08");
        var header = StructMarshal.Read<Th08StageHeader>(data.Slice(offset, headerSize));
        var fields = new Dictionary<string, object?>();
        return Stage(stageId, fields, raw, normalized, header, offset);
    }
}

internal sealed class Th09Format : OldFormat<Th09Header, Th09StageHeader>
{
    public override string Version => "TH09";
    protected override IReplayDecoder Decoder { get; } = new OldEncryptedReplayDecoder(
        requiredLength: 0xc0,
        packedEndOffset: 0x0c,
        outputLengthOffset: 0x1c,
        payloadOffset: 0xc0,
        keyOffset: 0x15,
        decryptStartOffset: 0x18);

    public override ReplayDocument Parse(ReadOnlySpan<byte> buffer)
    {
        var decoded = Decoder.Decode(buffer);
        return ParseDecoded(decoded.Envelope ?? decoded.Decoded, decoded.Decoded, buffer);
    }

    private ReplayDocument ParseDecoded(byte[] encrypted, byte[] data, ReadOnlySpan<byte> original)
    {
        ReplayDecoder.Require(encrypted, 0xc0);
        var header = ReadHeader(data);
        var stages = new List<ReplayStage>();
        var matchPlayer = ReplayDecoder.U32(encrypted, 0x44);
        if (matchPlayer != 0)
        {
            var playerOffset = RelativeOffset(matchPlayer);
            var opponentOffset = RelativeOffset(ReplayDecoder.U32(encrypted, 0x6c));
            stages.Add(ParseStage(data, 1, playerOffset, opponentOffset, opponentOffset));
        }
        else
        {
            var starts = new List<(int Stage, int Offset, int OpponentOffset)>();
            for (var i = 0; i < 9; i++)
            {
                var absolute = ReplayDecoder.U32(encrypted, 0x20 + i * 4);
                if (absolute == 0) continue;
                var opponentAbsolute = ReplayDecoder.U32(encrypted, 0x48 + i * 4);
                starts.Add((i + 1, RelativeOffset(absolute), RelativeOffset(opponentAbsolute)));
            }
            if (starts.Count == 0) throw new InvalidDataException("TH09 replay contains no player stage offsets.");
            var finalEnd = RelativeOffset(ReplayDecoder.U32(encrypted, 0x48));
            stages.AddRange(starts.Select((stage, index) => ParseStage(
                data,
                stage.Stage,
                stage.Offset,
                index + 1 < starts.Count ? starts[index + 1].Offset : finalEnd,
                stage.OpponentOffset)));
        }
        return CreatePropertySet(header, stages, original);

        static int RelativeOffset(uint absolute) => checked((int)absolute - 0xc0);
    }

    private static ReplayStage ParseStage(ReadOnlySpan<byte> data, int stageId, int offset, int end, int opponentOffset)
    {
        const int headerSize = 0x20;
        const int stride = 2;
        var (raw, normalized) = ReadFixedOldKeys(data, stageId, offset, end, headerSize, stride, "TH09");
        var header = StructMarshal.Read<Th09StageHeader>(data.Slice(offset, headerSize));
        ReplayDecoder.Require(data, opponentOffset + headerSize);
        var opponent = StructMarshal.Read<Th09StageHeader>(data.Slice(opponentOffset, headerSize));
        var fields = new Dictionary<string, object?>
        {
            ["OpponentCharacter"] = new SemanticField(opponent.Character, opponent.Character, "Opponent.Character", 0x06),
            ["PlayerTypeP1"] = new SemanticField(header._PlayerType, header._PlayerType, "PlayerTypeP1", 0x07),
            ["PlayerTypeP2"] = new SemanticField(opponent._PlayerType, opponent._PlayerType, "PlayerTypeP2", 0x07)
        };
        return Stage(stageId, fields, raw, normalized, header, offset);
    }
}

internal abstract class ModernFormat<THeader, TStage> : ReplayFormat<THeader, TStage> where THeader : unmanaged where TStage : unmanaged
{
    private int HeaderSize => Unsafe.SizeOf<THeader>();
    private int StageHeaderSize => Unsafe.SizeOf<TStage>();
    // Default modern replay envelope used by TH13/TH14/TH14.3/TH15/TH16/TH17/TH18,
    // and reused with small parameter overrides by TH10/TH11/TH12/TH12.5/TH12.8/TH09.5/TH20.
    protected override IReplayDecoder Decoder { get; } = new ModernReplayDecoder();
    public override ReplayDocument Parse(ReadOnlySpan<byte> buffer)
    {
        var decoded = Decoder.Decode(buffer);
        return ParseDecoded(decoded.Decoded, buffer);
    }

    protected virtual int StageCount(THeader header) => ReplayLayoutFields.StageCount(header);
    protected virtual int StageId(TStage stageHeader, int sequence) => ReplayLayoutFields.StageId(stageHeader, sequence);
    protected virtual int FrameCount(TStage stageHeader) => ReplayLayoutFields.FrameCount(stageHeader);
    protected virtual int PackedStageLength(TStage stageHeader) => ReplayLayoutFields.PackedLength(stageHeader);

    private ReplayDocument ParseDecoded(byte[] data, ReadOnlySpan<byte> original)
    {
        var header = ReadHeader(data);
        var count = StageCount(header);
        if (count is 0 or > 12)
            throw new InvalidDataException($"Implausible stage count: {count}.");

        var stages = new List<ReplayStage>();
        var stageHeaders = new List<object>();
        var stageOffset = HeaderSize;
        for (var index = 0; index < count; index++)
        {
            var stageHeader = ReadStageHeader(data.AsSpan(stageOffset));
            stageHeaders.Add(stageHeader);
            var frameCount = FrameCount(stageHeader);
            var packedStageLength = PackedStageLength(stageHeader);
            var replayOffset = stageOffset + StageHeaderSize;
            var hasSpecialFrameLayout = TryReadSpecialFrameLayout(data, stageOffset, stageHeader, out var specialFrameCount, out var specialPackedLength, out var specialReplayOffset, out var specialStride);
            if (hasSpecialFrameLayout)
            {
                frameCount = specialFrameCount;
                packedStageLength = specialPackedLength;
                replayOffset = specialReplayOffset;
            }
            if (frameCount < 0 || frameCount > 2_000_000)
                throw new InvalidDataException($"Implausible frame count at stage {index + 1}: {frameCount}.");

            var stride = hasSpecialFrameLayout ? specialStride
                : frameCount * 6 + DivideRoundUp(frameCount, 30) == packedStageLength ? 6
                : frameCount * 3 + DivideRoundUp(frameCount, 30) == packedStageLength ? 3
                : throw new InvalidDataException($"Stage {index + 1} has inconsistent frame length.");
            var readableFrameCount = frameCount;
            if (replayOffset + readableFrameCount * stride > data.Length)
            {
                if (!AllowTruncatedStage(index + 1, count))
                    ReplayDecoder.Require(data, replayOffset + readableFrameCount * stride);
                readableFrameCount = Math.Max(0, data.Length - replayOffset) / stride;
            }

            var keys = ReadKeys(data.AsSpan(replayOffset), readableFrameCount, stride);
            var stageId = StageId(stageHeader, index + 1);
            var fields = new Dictionary<string, object?>();
            ReplaySpecialFields.Enrich(Version, stageId, fields, StructMarshal.ToBytes(stageHeader), header);
            var fpsOffset = replayOffset + keys.BytesRead;
            var declaredFpsCount = DivideRoundUp(readableFrameCount, 30);
            var actualFpsCount = Math.Min(declaredFpsCount, Math.Max(0, data.Length - fpsOffset));
            var rawFps = data.AsSpan(fpsOffset, actualFpsCount).ToArray().ToList();
            stages.Add(new ReplayStage
            {
                StageId = stageId,
                Fields = fields,
                RawKeys = keys.RawKeys,
                Keys = keys.NormalizedKeys,
                Fps = rawFps.Select(value => (byte)(value & 0x7f)).ToList(),
                RawFps = rawFps,
                RawHeader = stageHeader,
                DecodedOffset = stageOffset
            });
            stageOffset += packedStageLength + StageHeaderSize;
        }
        ApplyPostStageFields(header, stageHeaders, stages);
        return CreatePropertySet(header, stages, original);
    }

    // Default contiguous stage frame layout used by TH10-TH18/TH20 normal stages.
    // TH09.5/TH16.5 override this because their frame area starts at a non-standard offset.
    public virtual bool TryReadSpecialFrameLayout(byte[] decoded, int stageOffset, TStage stageHeader, out int frameCount, out int packedLength, out int replayOffset, out int stride)
    {
        frameCount = packedLength = replayOffset = stride = 0;
        return false;
    }

    // Default strict length check used by TH10-TH18 and scene games; TH20 overrides for its truncated final stage sample.
    public virtual bool AllowTruncatedStage(int stageId, int stageCount) => false;

    // Default modern key stream used by TH11-TH18 and TH09.5/TH12.5/TH16.5.
    // TH10 and TH20 override because their press/release state transitions differ.
    public virtual ReplayKeyFrames ReadKeys(ReadOnlySpan<byte> data, int frameCount, int stride)
    {
        var storedFrameCount = frameCount;
        if (frameCount > 0 && (stride == 6 ? ReplayDecoder.U32(data, (frameCount - 1) * stride) : ReplayDecoder.U24(data, (frameCount - 1) * stride)) is 0xffffff or 0xffffffff)
            frameCount--;
        var rawKeys = new List<short>(frameCount);
        var normalized = new List<ReplayKey>(frameCount);
        for (var frame = 0; frame < frameCount; frame++)
        {
            var offset = frame * stride;
            var payload = stride == 6 ? ReplayDecoder.U32(data, offset) : ReplayDecoder.U24(data, offset);
            var rawInput = (ushort)(payload & 0xffff);
            rawKeys.Add(unchecked((short)rawInput));
            normalized.Add(Normalize(rawInput, payload));
        }
        return new ReplayKeyFrames(rawKeys, normalized, storedFrameCount * stride);
    }

    public virtual void ApplyPostStageFields(THeader header, IReadOnlyList<object> stageHeaders, IReadOnlyList<ReplayStage> stages)
    {
    }

    private static int DivideRoundUp(int value, int divisor) => (value + divisor - 1) / divisor;

    // Default action-bit mapping used by TH11 onward. TH10 overrides ShiftBit because its Shift action is bit 2.
    protected virtual int ShiftBit => 8;

    protected virtual ReplayKey Normalize(ushort inputState, uint payload)
    {
        var key = (ReplayKey)((inputState >> 4) & 0xf);
        var action = inputState & 0xf;
        if ((action & 1) != 0) key |= ReplayKey.Z;
        if ((action & 2) != 0) key |= ReplayKey.X;
        if ((action & ShiftBit) != 0) key |= ReplayKey.Shift;
        if (Version == "TH18")
        {
            if ((inputState & 0x400) != 0) key |= ReplayKey.C;
            if ((inputState & 0x800) != 0) key |= ReplayKey.D;
        }
        else
        {
            if ((inputState & 0x200) != 0) key |= ReplayKey.Ctrl;
            if ((inputState & 0x800) != 0) key |= ReplayKey.C;
        }
        if (Version == "TH10" && (inputState & 0x100) != 0) key |= ReplayKey.Ctrl;
        return key;
    }
}
internal sealed class Th10Format : ModernFormat<Th10Header, Th10Stage>
{
    public override string Version => "TH10";
    protected override IReplayDecoder Decoder { get; } = new ModernReplayDecoder(base1: 0xaa, block2: 0x80, base2: 0x3d, add2: 0x7a);
    protected override int ShiftBit => 4;

}

internal sealed class AlcostgFormat : ModernFormat<AlcostgHeader, AlcostgStage>
{
    public override string Version => "alcostg";
    protected override IReplayDecoder Decoder { get; } = new ModernReplayDecoder(
        block1: 0x400, base1: 0xaa, add1: 0xe1,
        block2: 0x80, base2: 0x3d, add2: 0x7a);

    public override bool TryReadSpecialFrameLayout(byte[] decoded, int stageOffset, AlcostgStage stageHeader,
        out int frameCount, out int packedLength, out int replayOffset, out int stride)
    {
        frameCount = checked((int)stageHeader.FrameCount);
        packedLength = checked((int)stageHeader.PackedLength);
        replayOffset = stageOffset + Unsafe.SizeOf<AlcostgStage>();
        stride = 8;
        return true;
    }

    public override ReplayKeyFrames ReadKeys(ReadOnlySpan<byte> data, int frameCount, int stride)
    {
        var raw = new List<short>(frameCount);
        var normalized = new List<ReplayKey>(frameCount);
        for (var frame = 0; frame < frameCount; frame++)
        {
            var input = ReplayDecoder.U16(data, frame * stride);
            raw.Add(unchecked((short)input));
            var key = (ReplayKey)((input >> 4) & 0xf);
            if ((input & (1 << 0)) != 0) key |= ReplayKey.Z;
            if ((input & (1 << 1)) != 0) key |= ReplayKey.X;
            if ((input & (1 << 2)) != 0) key |= ReplayKey.C;
            if ((input & (1 << 3)) != 0) key |= ReplayKey.Shift;
            if ((input & (1 << 9)) != 0) key |= ReplayKey.Ctrl;
            normalized.Add(key);
        }
        return new ReplayKeyFrames(raw, normalized, frameCount * stride);
    }
}
internal sealed class Th11Format : ModernFormat<Th11Header, Th11Stage> { public override string Version => "TH11"; protected override IReplayDecoder Decoder { get; } = new ModernReplayDecoder(block1: 0x800, base1: 0xaa, block2: 0x40, base2: 0x3d, add2: 0x7a); }
internal sealed class Th12Format : ModernFormat<Th12Header, Th12Stage> { public override string Version => "TH12"; protected override IReplayDecoder Decoder { get; } = new ModernReplayDecoder(block1: 0x800, base1: 0x5e, block2: 0x40); }
internal sealed class Th125Format : ModernFormat<Th125Header, Th125Stage>
{
    public override string Version => "TH12.5";
    protected override IReplayDecoder Decoder { get; } = new ModernReplayDecoder(block1: 0x800, base1: 0x5e, block2: 0x40);
    public override ReplayKeyFrames ReadKeys(ReadOnlySpan<byte> data, int frameCount, int stride)
    {
        var storedFrameCount = frameCount;
        if (frameCount > 0 && ReplayDecoder.U24(data, (frameCount - 1) * stride) == 0xffffff) frameCount--;
        var raw = new List<short>(frameCount); var normalized = new List<ReplayKey>(frameCount);
        for (var frame = 0; frame < frameCount; frame++)
        {
            var input = (byte)ReplayDecoder.U24(data, frame * stride);
            raw.Add(input);
            var key = ReplayKey.None;
            if ((input & 0x01) != 0) key |= ReplayKey.Z;
            if ((input & 0x02) != 0) key |= ReplayKey.X;
            if ((input & 0x04) != 0) key |= ReplayKey.Shift;
            if ((input & 0x08) != 0) key |= ReplayKey.Up;
            if ((input & 0x10) != 0) key |= ReplayKey.Down;
            if ((input & 0x20) != 0) key |= ReplayKey.Left;
            if ((input & 0x40) != 0) key |= ReplayKey.Right;
            if ((input & 0x80) != 0) key |= ReplayKey.Ctrl;
            normalized.Add(key);
        }
        return new ReplayKeyFrames(raw, normalized, storedFrameCount * stride);
    }
}
internal sealed class Th128Format : ModernFormat<Th128Header, Th128Stage> { public override string Version => "TH12.8"; protected override IReplayDecoder Decoder { get; } = new ModernReplayDecoder(block1: 0x800, base1: 0x5e, add1: 0xe7, block2: 0x80, add2: 0x36); }
internal sealed class Th13Format : ModernFormat<Th13Header, Th13Stage> { public override string Version => "TH13"; }
internal sealed class Th14Format : ModernFormat<Th14Header, Th14Stage> { public override string Version => "TH14"; }
internal sealed class Th143Format : ModernFormat<Th143Header, Th143Stage> { public override string Version => "TH14.3"; }
internal sealed class Th15Format : ModernFormat<Th15Header, Th15Stage> { public override string Version => "TH15"; }
internal sealed class Th16Format : ModernFormat<Th16Header, Th16Stage> { public override string Version => "TH16"; }
internal sealed class Th165Format : ModernFormat<Th165Header, Th165Stage> { public override string Version => "TH16.5"; }
internal sealed class Th17Format : ModernFormat<Th17Header, Th17Stage> { public override string Version => "TH17"; }
internal sealed class Th18Format : ModernFormat<Th18Header, Th18Stage> { public override string Version => "TH18"; }
internal sealed class Th17TrialFormat : ModernFormat<Th17TrialHeader, Th17TrialStage> { public override string Version => "TH17Trial"; }
internal sealed class Th18TrialFormat : ModernFormat<Th18TrialHeader, Th18TrialStage> { public override string Version => "TH18Trial"; }
internal sealed class Th20Format : ModernFormat<Th20Header, Th20Stage>
{
    public override string Version => "TH20";
    protected override IReplayDecoder Decoder { get; } = new ModernReplayDecoder(dataOffset: 0x30, packedLengthOffset: 0x28, unpackedLengthOffset: 0x2c);
    public override bool AllowTruncatedStage(int stageId, int stageCount) => stageId == stageCount;

    public override ReplayKeyFrames ReadKeys(ReadOnlySpan<byte> data, int frameCount, int stride)
    {
        var storedFrameCount = frameCount;
        if (frameCount > 0 && ReplayDecoder.U32(data, (frameCount - 1) * stride) == uint.MaxValue) frameCount--;
        var rawKeys = new List<short>(frameCount);
        var normalized = new List<ReplayKey>(frameCount);
        for (var frame = 0; frame < frameCount; frame++)
        {
            var offset = frame * stride;
            var payload = ReplayDecoder.U32(data, offset);
            var rawInput = (ushort)(payload & 0xffff);
            rawKeys.Add(unchecked((short)rawInput));
            var key = (ReplayKey)((rawInput >> 4) & 0xf);
            if ((rawInput & 0x01) != 0) key |= ReplayKey.Z;
            if ((rawInput & 0x04) != 0) key |= ReplayKey.X;
            if ((rawInput & 0x08) != 0) key |= ReplayKey.Shift;
            normalized.Add(key);
        }
        return new ReplayKeyFrames(rawKeys, normalized, storedFrameCount * stride);
    }

    public override void ApplyPostStageFields(Th20Header header, IReadOnlyList<object> stageHeaders, IReadOnlyList<ReplayStage> stages)
    {
        for (var index = 0; index < stages.Count; index++)
        {
            var endScore = index + 1 < stageHeaders.Count ? ReplayLayoutFields.UInt64Field(stageHeaders[index + 1], "Score") : ReplayLayoutFields.UInt64Field(header, "TotalScore");
            if (endScore is ulong rawScore) stages[index].Fields["Score"] = new SemanticField(rawScore, rawScore, "Score");
        }
    }
}
internal sealed class Th095Format : ModernFormat<Th95Header, Th95Stage>
{
    public override string Version => "TH09.5";
    protected override IReplayDecoder Decoder { get; } = new ModernReplayDecoder(base1: 0xaa, block2: 0x80, base2: 0x3d, add2: 0x7a);
    public override bool TryReadSpecialFrameLayout(byte[] decoded, int stageOffset, Th95Stage stageHeader, out int frameCount, out int packedLength, out int replayOffset, out int stride)
    {
        packedLength = ReplayLayoutFields.PackedLength(stageHeader); frameCount = packedLength / 6 - 2; replayOffset = stageOffset + Unsafe.SizeOf<Th95Stage>(); stride = 6;
        return true;
    }
    protected override ReplayKey Normalize(ushort inputState, uint payload)
    {
        var key = (ReplayKey)((inputState >> 4) & 0xf);
        if ((inputState & 0x002) != 0) key |= ReplayKey.Z;
        if ((inputState & 0x001) != 0) key |= ReplayKey.X;
        if ((inputState & 0x004) != 0) key |= ReplayKey.Shift;
        if ((inputState & 0x100) != 0) key |= ReplayKey.Ctrl;
        return key;
    }
}
