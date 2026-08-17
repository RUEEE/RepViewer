using System.Collections;
using System.Globalization;
using System.Text;
using RepViewer.Core;

namespace RepViewer.Presentation;

public sealed record FieldPresentation(string Label, string? Format = null, IReadOnlyDictionary<string, string>? Values = null, string? ValuesFrom = null);
public sealed record DisplayField(string Path, string Label, string RawField, string Text, object? SemanticValue, object? RawValue);

public sealed class PresentationCatalog
{
    private readonly Dictionary<string, string> _ui;
    private readonly Dictionary<string, FieldPresentation> _fields;

    private PresentationCatalog(string locale, Dictionary<string, string> ui, Dictionary<string, FieldPresentation> fields)
    {
        Locale = locale;
        _ui = ui;
        _fields = fields;
    }

    public string Locale { get; }
    public IReadOnlyDictionary<string, string> Ui => _ui;
    public string Text(string key) => _ui.GetValueOrDefault(key, key);

    public bool HasField(string path)
    {
        var name = path[(path.LastIndexOf('.') + 1)..];
        return _fields.ContainsKey(path) || _fields.ContainsKey(name);
    }

    public FieldPresentation Field(string path)
    {
        var name = path[(path.LastIndexOf('.') + 1)..];
        var field = _fields.GetValueOrDefault(path) ?? _fields.GetValueOrDefault(name) ?? new FieldPresentation(name);
        return field.Values is null && field.ValuesFrom is { } source && _fields.GetValueOrDefault(source)?.Values is { } values
            ? field with { Values = values } : field;
    }

    public DisplayField Display(string path, object? semanticValue, object? rawValue = null, string? sourceFieldName = null, int? offset = null)
    {
        var field = Field(path);
        var source = string.IsNullOrWhiteSpace(sourceFieldName) ? path[(path.LastIndexOf('.') + 1)..] : sourceFieldName;
        var rawField = offset is { } value ? $"{source}（+0x{value:X}）" : source;
        return new DisplayField(path, field.Label, rawField, DisplayFormatter.Format(semanticValue, field), semanticValue, rawValue ?? semanticValue);
    }

    public IEnumerable<DisplayField> General(ReplayDocument replay) =>
        replay.General.Children.Where(node => !node.IsUnknown && node.Kind == ReplayNodeType.Value)
            .Select(node => Display($"general.{node.Name}", node.SemanticValue, node.RawValue, node.SourceFieldName, node.Offset));

    public IEnumerable<DisplayField> Stage(ReplayDocument replay, int index)
    {
        var node = replay.StageRoot.Children[index];
        return node.Children.Where(child => !child.IsUnknown && child.Kind == ReplayNodeType.Value)
            .Select(child => Display($"stage.{child.Name}", child.SemanticValue, child.RawValue, child.SourceFieldName, child.Offset));
    }

    public static PresentationCatalog Load(string root, string locale, string gameId)
    {
        var fallback = Path.Combine(root, "en-US");
        var selected = ResolveLocale(root, locale) ?? (Directory.Exists(fallback) ? fallback : throw new DirectoryNotFoundException(root));
        var ui = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var fields = new Dictionary<string, FieldPresentation>(StringComparer.OrdinalIgnoreCase);
        MergeFile(Path.Combine(fallback, "main.yaml"), ui, fields);
        if (!Path.GetFullPath(selected).Equals(Path.GetFullPath(fallback), StringComparison.OrdinalIgnoreCase))
            MergeFile(Path.Combine(selected, "main.yaml"), ui, fields);
        MergeFile(Path.Combine(fallback, $"{gameId.ToLowerInvariant()}.yaml"), ui, fields);
        if (!Path.GetFullPath(selected).Equals(Path.GetFullPath(fallback), StringComparison.OrdinalIgnoreCase))
            MergeFile(Path.Combine(selected, $"{gameId.ToLowerInvariant()}.yaml"), ui, fields);
        return new PresentationCatalog(Path.GetFileName(selected), ui, fields);
    }

    public static string DefaultRoot => Path.Combine(AppContext.BaseDirectory, "presentation");

    private static string? ResolveLocale(string root, string locale)
    {
        foreach (var candidate in new[] { locale, locale.Replace('_', '-'), locale.Replace('-', '_') })
        {
            var path = Path.Combine(root, candidate);
            if (Directory.Exists(path)) return path;
        }
        var language = locale.Split('-', '_')[0];
        return Directory.Exists(root) ? Directory.EnumerateDirectories(root).FirstOrDefault(path => Path.GetFileName(path).StartsWith(language, StringComparison.OrdinalIgnoreCase)) : null;
    }

    private static void MergeFile(string path, Dictionary<string, string> ui, Dictionary<string, FieldPresentation> fields)
    {
        if (!File.Exists(path)) return;
        var yaml = SimpleYaml.Read(File.ReadAllText(path));
        if (Map(yaml, "ui") is { } uiMap)
            foreach (var pair in Flatten(uiMap)) ui[pair.Key] = Convert.ToString(pair.Value, CultureInfo.InvariantCulture) ?? "";
        if (Map(yaml, "fields") is not { } fieldMap) return;
        foreach (var pair in fieldMap)
        {
            if (pair.Value is string scalar) { fields[pair.Key] = new FieldPresentation(scalar); continue; }
            if (pair.Value is not Dictionary<string, object> map) continue;
            var label = Convert.ToString(map.GetValueOrDefault("label"), CultureInfo.InvariantCulture) ?? pair.Key;
            var format = Convert.ToString(map.GetValueOrDefault("format"), CultureInfo.InvariantCulture);
            var valuesFrom = Convert.ToString(map.GetValueOrDefault("valuesFrom"), CultureInfo.InvariantCulture);
            var values = Map(map, "values")?.ToDictionary(item => item.Key, item => Convert.ToString(item.Value, CultureInfo.InvariantCulture) ?? "", StringComparer.OrdinalIgnoreCase);
            fields[pair.Key] = new FieldPresentation(label, format, values, valuesFrom);
        }
    }

    private static Dictionary<string, object>? Map(Dictionary<string, object> map, string key) => map.GetValueOrDefault(key) as Dictionary<string, object>;
    private static IEnumerable<KeyValuePair<string, object>> Flatten(Dictionary<string, object> map, string prefix = "")
    {
        foreach (var pair in map)
        {
            var key = prefix.Length == 0 ? pair.Key : $"{prefix}.{pair.Key}";
            if (pair.Value is Dictionary<string, object> child)
                foreach (var nested in Flatten(child, key)) yield return nested;
            else yield return new KeyValuePair<string, object>(key, pair.Value);
        }
    }
}

public static class DisplayFormatter
{
    public static string Format(object? value, FieldPresentation presentation)
    {
        if (value is null) return "—";
        var key = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        if (presentation.Values?.TryGetValue(key, out var mapped) == true) return mapped;
        var format = presentation.Format?.ToLowerInvariant();
        return format switch
        {
            "details" => "...",
            "enum-list" when value is IEnumerable items => string.Join(",", items.Cast<object?>().Select(item =>
            {
                var itemKey = Convert.ToString(item, CultureInfo.InvariantCulture) ?? "";
                return presentation.Values?.GetValueOrDefault(itemKey) ?? itemKey;
            })),
            "cards" when value is IEnumerable<ReplayCard> cards => string.Join("; ", cards.Select(card =>
                $"{presentation.Values?.GetValueOrDefault(card.CardId.ToString(CultureInfo.InvariantCulture)) ?? card.CardId.ToString(CultureInfo.InvariantCulture)}({ReplayFrameTime.Format(card.CooldownFrames)})")),
            "number" => value is IFormattable number ? number.ToString("N0", CultureInfo.CurrentCulture) : key,
            "decimal" => value is IFormattable decimalNumber ? decimalNumber.ToString("N2", CultureInfo.CurrentCulture) : key,
            "percent" => value is IConvertible ? $"{Convert.ToDouble(value, CultureInfo.InvariantCulture):0.##}%" : key,
            "duration" => value is TimeSpan span ? ReplayFrameTime.Format(checked((int)Math.Round(span.TotalSeconds * 60))) : key,
            "datetime" => value is DateTimeOffset timestamp ? timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture) : key,
            "hex" => value is IConvertible ? $"0x{Convert.ToUInt64(value, CultureInfo.InvariantCulture):X}" : key,
            _ => value is IEnumerable sequence and not string ? $"[{sequence.Cast<object?>().Count()}]" : Convert.ToString(value, CultureInfo.CurrentCulture) ?? ""
        };
    }

    public static string FormatRaw(object? value) => value switch
    {
        null => "—",
        byte[] bytes => $"{Convert.ToHexString(bytes)}  [{Printable(bytes)}]",
        IEnumerable<byte> bytes => Convert.ToHexString(bytes.ToArray()),
        IEnumerable sequence and not string => string.Join(", ", sequence.Cast<object?>().Select(item => Convert.ToString(item, CultureInfo.InvariantCulture))),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
    };

    private static string Printable(byte[] bytes)
    {
        var end = Array.IndexOf(bytes, (byte)0);
        var payload = end < 0 ? bytes : bytes[..end];
        try { return ReplayTextEncoding.ShiftJis.GetString(payload); }
        catch { return Encoding.UTF8.GetString(payload); }
    }
}
