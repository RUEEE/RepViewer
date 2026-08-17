using System.Buffers.Binary;

namespace RepViewer.Core;

public sealed record ReplaySpellTime(int RawValue, decimal Seconds);

public sealed record ReplayCard(int CardId, int CooldownFrames)
{
    public decimal CooldownSeconds => CooldownFrames / 60m;
}

internal static class ReplaySpecialFields
{
    private static readonly IReadOnlyDictionary<string, (int Offset, bool Th11)> SpellTimeLayouts =
        new Dictionary<string, (int, bool)>(StringComparer.OrdinalIgnoreCase)
        {
            ["TH11"] = (0x3c, true), ["TH12"] = (0x4c, false), ["TH12.8"] = (0x30, false),
            ["TH13"] = (0x70, false), ["TH14"] = (0x88, false), ["TH15"] = (0x1e4, false),
            ["TH16"] = (0x240, false), ["TH17"] = (0x104, false), ["TH17Trial"] = (0x104, false), ["TH18"] = (0x18, false), ["TH18Trial"] = (0x114, false),
            ["TH20"] = (0x1c, false)
        };

    public static void Enrich(string formatId, int stageId, Dictionary<string, object?> fields, ReadOnlySpan<byte> header, object? replayHeader = null)
    {
        if (SpellTimeLayouts.TryGetValue(formatId, out var layout))
        {
            var raw = ReadInt32Array(header, layout.Offset, 20);
            var semantic = new List<ReplaySpellTime>();
            foreach (var value in raw)
            {
                var seconds = layout.Th11 ? DecodeTh11SpellTime(value) : DecodeSpellTime(value);
                if (seconds is null) break;
                semantic.Add(new ReplaySpellTime(value, seconds.Value));
            }
            fields["SpellTimes"] = new SemanticField(semantic.AsReadOnly(), raw, "_SpellTimesRaw", layout.Offset);
        }

        if (formatId.Equals("TH18", StringComparison.OrdinalIgnoreCase) || formatId.Equals("TH18Trial", StringComparison.OrdinalIgnoreCase))
        {
            fields["Cards"] = CardField(header, 0x164, 0x564, "_CardIds");
            if (formatId.Equals("TH18", StringComparison.OrdinalIgnoreCase) && stageId == 7) fields["CardsAfterShop"] = CardField(header, 0xa64, 0xe64, "_CardsAfterShopIds");
        }

        if (formatId is "TH10" or "TH11" && header.Length >= 0x14)
        {
            var rawPower = unchecked((uint)ReadInt32(header, 0x10));
            var divisor = 20m;
            if (formatId == "TH11" && HeaderUInt(replayHeader, "Character") == 1 && HeaderUInt(replayHeader, "ShotType") == 0)
                divisor = 12m;
            var semantic = divisor == 12m
                ? Math.Floor(rawPower / divisor * 100m) / 100m
                : decimal.Round(rawPower / divisor, 2, MidpointRounding.AwayFromZero);
            fields["Power"] = new SemanticField(semantic, rawPower, "Power", 0x10);
        }


        var position = PositionLayout(formatId);
        if (position is not null && header.Length >= position.Value.YOffset + 4)
        {
            var rawX = ReadInt32(header, position.Value.XOffset);
            var rawY = ReadInt32(header, position.Value.YOffset);
            fields["XPosition"] = new SemanticField(rawX / position.Value.Divisor + 224m, rawX, "XPositionRaw", position.Value.XOffset);
            fields["YPosition"] = new SemanticField(rawY / position.Value.Divisor + 16m, rawY, "YPositionRaw", position.Value.YOffset);
        }
    }

    private static SemanticField CardField(ReadOnlySpan<byte> header, int idOffset, int cooldownOffset, string source)
    {
        var cards = new List<ReplayCard>();
        var raw = new List<(int CardId, int CooldownFrames)>();
        for (var index = 0; index < 256; index++)
        {
            var id = ReadInt32(header, idOffset + index * 4);
            if (id == -1) break;
            var cooldown = ReadInt32(header, cooldownOffset + index * 4);
            raw.Add((id, cooldown));
            cards.Add(new ReplayCard(id, cooldown));
        }
        return new SemanticField(cards.AsReadOnly(), raw.AsReadOnly(), source, idOffset);
    }

    private static int[] ReadInt32Array(ReadOnlySpan<byte> data, int offset, int count)
    {
        if (offset < 0 || offset > data.Length - count * 4) return [];
        var values = new int[count];
        for (var index = 0; index < count; index++) values[index] = ReadInt32(data, offset + index * 4);
        return values;
    }

    private static int ReadInt32(ReadOnlySpan<byte> data, int offset) =>
        offset >= 0 && offset <= data.Length - 4 ? BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4)) : 0;

    private static uint? HeaderUInt(object? header, string name)
    {
        var value = header?.GetType().GetField(name)?.GetValue(header);
        if (value is null) return null;
        try { return Convert.ToUInt32(value, System.Globalization.CultureInfo.InvariantCulture); }
        catch { return null; }
    }

    private static (int XOffset, int YOffset, decimal Divisor)? PositionLayout(string formatId) => formatId switch
    {
        "TH10" => (0x24, 0x28, 100m),
        "TH11" => (0x20, 0x24, 128m),
        "TH12" => (0x30, 0x34, 128m),
        "TH12.8" => (0x18, 0x1c, 128m),
        "TH13" or "TH14" or "TH15" or "TH16" or "TH17" or "TH17Trial" or "TH18" or "TH18Trial" => (0x0c, 0x10, 128m),
        "TH20" => (0x10, 0x14, 128m),
        _ => null
    };

    private static decimal? DecodeSpellTime(int raw)
    {
        if (raw == 0) return null;
        var decimalPart = raw % 100 - 33;
        if (decimalPart < 0) decimalPart += 100;
        var integerPart = raw / 100 % 1000 - 66;
        if (integerPart < 0) integerPart += 1000;
        var checksum = raw / 100000;
        if (decimalPart + integerPart + 22 == checksum) return integerPart + decimalPart / 100m;
        if (raw < 0)
        {
            decimalPart = raw % 100 + 67;
            if (decimalPart < 0) decimalPart += 100;
        }
        var temporary = (raw - decimalPart) / 100 - 22000 - 1000 * decimalPart;
        integerPart = temporary / 1000;
        if (temporary > -66000) integerPart--;
        return integerPart < 0 && integerPart * 1000 + (integerPart + 66) % 1000 == temporary
            ? integerPart + decimalPart / 100m : null;
    }

    private static decimal? DecodeTh11SpellTime(int raw)
    {
        if (raw == 0) return null;
        var decimalPart = raw % 100 - 33;
        if (decimalPart < 0) decimalPart += 100;
        var integerPart = raw / 100 % 1000 - 66;
        if (integerPart < 0) integerPart += 1000;
        var checksum = raw / 100000;
        if (decimalPart + integerPart + 22 == checksum) return integerPart + decimalPart / 100m;
        if (raw < 0)
        {
            decimalPart = raw % 100 + 67;
            if (decimalPart < 0) decimalPart += 100;
        }
        var temporary = (raw - decimalPart) / 100 - 22066 - 1000 * decimalPart;
        integerPart = temporary / 1001;
        return temporary % 1001 == 0 && integerPart <= 999 ? integerPart + decimalPart / 100m : null;
    }
}
