using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RepViewer.Core;

public sealed class ReplayDocument
{
    private ReplayDocument(ReplayIdentity identity, ReplayPropertyNode general, ReplayPropertyNode stageRoot,
        IReadOnlyList<ReplayStage> stages, byte[] originalBytes)
    {
        Identity = identity;
        General = general;
        StageRoot = stageRoot;
        Stages = stages;
        OriginalBytes = originalBytes;
    }

    internal static ReplayDocument Create(string formatId, object header, List<ReplayStage> stages, ReadOnlySpan<byte> original)
    {
        var builder = new ReplayPropertyTreeBuilder(formatId);
        var general = builder.Group("general", [], header);
        var stageRoot = new ReplayPropertyNode { Name = "stages", Kind = ReplayNodeType.Array, SemanticValue = stages, RawValue = stages };
        foreach (var stage in stages)
        {
            var node = builder.Group($"stage.{stage.StageId}", stage.Fields, stage.RawHeader);
            AddStageSyntheticNodes(node, stage);
            stageRoot.Children.Add(node);
        }
        AddGeneralSyntheticNodes(formatId, general, stages, original);
        var gameId = formatId.ToLowerInvariant().Replace("trial", "", StringComparison.Ordinal).Replace(".", "", StringComparison.Ordinal);
        var document = new ReplayDocument(new ReplayIdentity(gameId, formatId, formatId.Contains("trial", StringComparison.OrdinalIgnoreCase)),
            general, stageRoot, stages.AsReadOnly(), original.ToArray());
        document.UserData = ReplayUserData.TryRead(original);
        document.ModMetadata = ReplayModMetadata.TryRead(original, document.UserData);
        return document;
    }

    public ReplayIdentity Identity { get; }
    public ReplayPropertyNode General { get; }
    public ReplayPropertyNode StageRoot { get; }
    public IReadOnlyList<ReplayStage> Stages { get; }
    public ReadOnlyMemory<byte> OriginalBytes { get; }
    public ReplayUserData? UserData { get; private set; }
    public ReplayModMetadata? ModMetadata { get; private set; }
    public List<ReplayIssue> Issues { get; } = [];

    public byte[] WithComment(string comment, System.Text.Encoding encoding) =>
        (UserData ?? throw new NotSupportedException("This replay has no writable USER block area."))
        .WithComment(OriginalBytes.Span, comment, encoding);

    private static void AddGeneralSyntheticNodes(string formatId, ReplayPropertyNode root, IReadOnlyList<ReplayStage> stages, ReadOnlySpan<byte> original)
    {
        var canonical = formatId.EndsWith("Trial", StringComparison.OrdinalIgnoreCase) ? formatId[..^5] : formatId;
        if (canonical is not ("TH09.5" or "TH12.5" or "TH14.3" or "TH16.5") && !HasField(root, "StageCount"))
            root.Children.Add(ValueNode("StageCount", stages.Count));
        var totalFrames = stages.Sum(stage => stage.Keys.Count);
        for (var index = 0; index < stages.Count; index++)
            root.Children.Add(ValueNode($"Stage{index + 1}Duration", ReplayFrameTime.FromFrames(stages[index].Keys.Count), stages[index].Keys.Count));
        root.Children.Add(ValueNode("TotalDuration", ReplayFrameTime.FromFrames(totalFrames), totalFrames));
        root.Children.Add(ArrayNode("RawFile", original.ToArray()));
        SortChildren(root);
    }

    private static void AddStageSyntheticNodes(ReplayPropertyNode node, ReplayStage stage)
    {
        if (!HasField(node, "StageNumber")) node.Children.Add(ValueNode("StageNumber", stage.StageId));
        if (!HasField(node, "FrameCount")) node.Children.Add(ValueNode("FrameCount", stage.Keys.Count));
        node.Children.Add(ValueNode("Duration", ReplayFrameTime.FromFrames(stage.Keys.Count), stage.Keys.Count));
        node.Children.Add(ArrayNode("RawKeys", stage.RawKeys));
        if (stage.RawFps is not null) node.Children.Add(ArrayNode("RawFps", stage.RawFps));
        SortChildren(node);
    }

    private static bool HasField(ReplayPropertyNode group, string name) => group.Children.Any(n => n.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    private static ReplayPropertyNode ValueNode(string name, object? value) => new() { Name = name, Kind = ReplayNodeType.Value, SemanticValue = value, RawValue = value, SourceFieldName = name };
    private static ReplayPropertyNode ValueNode(string name, object? value, object? rawValue) => new() { Name = name, Kind = ReplayNodeType.Value, SemanticValue = value, RawValue = rawValue, SourceFieldName = name };
    private static ReplayPropertyNode ArrayNode(string name, object values) => new() { Name = name, Kind = ReplayNodeType.Array, SemanticValue = values, RawValue = values };
    private static void SortChildren(ReplayPropertyNode node)
    {
        var sorted = node.Children.OrderBy(n => n.Name == "unknown" ? 1 : n.Name.StartsWith("Raw", StringComparison.Ordinal) ? 2 : 0)
            .ThenBy(n => n.Offset ?? int.MaxValue).ToArray();
        node.Children.Clear();
        node.Children.AddRange(sorted);
    }
}

internal sealed class ReplayPropertyTreeBuilder
{
    private readonly string _formatId;

    public ReplayPropertyTreeBuilder(string formatId) => _formatId = formatId;

    public ReplayPropertyNode Group(string name, IEnumerable<KeyValuePair<string, object?>> fields, object? rawStruct = null)
    {
        var group = new ReplayPropertyNode { Name = name, Kind = ReplayNodeType.Group };
        var nodes = rawStruct is null ? fields.Select(f => Node(f.Key, f.Value, (f.Key, null))) : StructBackedNodes(fields, rawStruct);
        group.Children.AddRange(nodes.OrderBy(n => n.Name == "unknown" ? 1 : 0).ThenBy(n => n.Offset ?? int.MaxValue));
        return group;
    }

    private IEnumerable<ReplayPropertyNode> StructBackedNodes(IEnumerable<KeyValuePair<string, object?>> fields, object rawStruct)
    {
        var remaining = new Dictionary<string, object?>(fields, StringComparer.OrdinalIgnoreCase);
        var unknown = CreateUnknown(rawStruct);
        if (remaining.Remove("unknown", out var supplied) && supplied is UnknownField extra)
        {
            unknown.UnknownValues.AddRange(extra.UnknownValues);
            foreach (var pair in extra.KnownValues) unknown.KnownValues[pair.Key] = pair.Value;
        }
        var hidden = unknown.KnownValues.Values.OfType<SemanticField>().Concat(remaining.Values.OfType<SemanticField>())
            .Select(f => f.SourceFieldName).Where(n => !string.IsNullOrEmpty(n)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        AddCoupledCardSources(hidden);
        foreach (var known in remaining.Values.OfType<SemanticField>())
        {
            var length = RawLength(known.RawValue);
            if (known.Offset is not { } start || length <= 0) continue;
            unknown.UnknownValues.RemoveAll(value => value.Offset < start + length && value.Offset + value.Data.Length > start);
        }
        var type = rawStruct.GetType();
        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public).OrderBy(f => Marshal.OffsetOf(type, f.Name).ToInt32()))
        {
            if (field.Name.StartsWith("_", StringComparison.Ordinal) || hidden.Contains(field.Name)) continue;
            var offset = Marshal.OffsetOf(type, field.Name).ToInt32();
            var value = remaining.Remove(field.Name, out var mapped) ? mapped : StructFieldValue(rawStruct, field, offset);
            if (value is not SemanticField)
                value = ReplaySemanticRules.Convert(_formatId, field.Name, value, offset);
            yield return Node(field.Name, value, (field.Name, offset));
        }
        yield return Node("unknown", unknown, ("unknown", null));
        foreach (var pair in remaining) yield return Node(pair.Key, pair.Value, (pair.Key, null));
    }

    private static UnknownField CreateUnknown(object rawStruct)
    {
        var method = typeof(StructMarshal).GetMethod(nameof(StructMarshal.Unknown))!;
        return (UnknownField)method.MakeGenericMethod(rawStruct.GetType()).Invoke(null, [rawStruct])!;
    }

    private static object? StructFieldValue(object rawStruct, FieldInfo field, int offset)
    {
        var fixedBuffer = field.GetCustomAttribute<FixedBufferAttribute>();
        if (fixedBuffer is null) return field.GetValue(rawStruct);
        var method = typeof(StructMarshal).GetMethod(nameof(StructMarshal.ToBytes))!;
        var data = (byte[])method.MakeGenericMethod(rawStruct.GetType()).Invoke(null, [rawStruct])!;
        return data.AsSpan(offset, fixedBuffer.Length * Marshal.SizeOf(fixedBuffer.ElementType)).ToArray();
    }

    private static int RawLength(object? value) => value switch
    {
        byte[] values => values.Length,
        short[] values => values.Length * 2,
        ushort[] values => values.Length * 2,
        int[] values => values.Length * 4,
        uint[] values => values.Length * 4,
        long[] values => values.Length * 8,
        ulong[] values => values.Length * 8,
        byte or sbyte => 1,
        short or ushort => 2,
        int or uint or float => 4,
        long or ulong or double => 8,
        _ => 0
    };

    private static void AddCoupledCardSources(HashSet<string?> sources)
    {
        if (sources.Contains("_CardIds")) sources.Add("_CardCooldownFrames");
        if (sources.Contains("_CardsAfterShopIds")) sources.Add("_CardsAfterShopCooldownFrames");
    }

    private static ReplayPropertyNode Node(string name, object? value, (string Name, int? Offset) raw) => value switch
    {
        SemanticField field => new ReplayPropertyNode { Name = name, Kind = ReplayNodeType.Value, SemanticValue = field.Value, RawValue = field.RawValue, SourceFieldName = field.SourceFieldName ?? raw.Name, Offset = field.Offset ?? raw.Offset },
        UnknownField field => new ReplayPropertyNode { Name = name, Kind = ReplayNodeType.Group, SemanticValue = field, RawValue = field.Data, IsUnknown = true },
        RawArrayField array => new ReplayPropertyNode { Name = name, Kind = ReplayNodeType.Array, SemanticValue = array.Values, RawValue = array.Values },
        _ => new ReplayPropertyNode { Name = name, Kind = ReplayNodeType.Value, SemanticValue = value, RawValue = value, SourceFieldName = raw.Name, Offset = raw.Offset }
    };
}
