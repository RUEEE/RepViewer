using RepViewer.Core;
using RepViewer.Presentation;

namespace RepViewer.Plugins;

internal sealed class OverviewPlugin : IReplayViewPlugin
{
    public string Id => "builtin.overview";
    public int Order => 0;
    public bool CanCreate(ReplayPluginContext context) => true;
    public IReadOnlyList<ReplayView> Create(ReplayPluginContext context)
    {
        var fields = context.StageIndex is { } index ? context.Presentation.Stage(context.Replay, index) : context.Presentation.General(context.Replay);
        var rows = fields.Select(field => new ReplayViewRow(field.Path, [field.Label, field.RawField, field.Text, DisplayFormatter.FormatRaw(field.RawValue)])).ToArray();
        return [new ReplayView("overview", context.StageIndex is null ? "section.general" : "section.stage", ReplayViewKind.Properties,
            [new("field", context.Presentation.Text("column.field")), new("rawField", context.Presentation.Text("column.rawField")),
             new("value", context.Presentation.Text("column.value")), new("raw", context.Presentation.Text("column.raw"))], rows,
            Metadata: new Dictionary<string, object?>
            {
                ["propertyRoot"] = context.StageIndex is { } stage ? context.Replay.StageRoot.Children[stage] : context.Replay.General,
                ["isGeneral"] = context.StageIndex is null, ["replay"] = context.Replay
            })];
    }
}

internal sealed class FpsPlugin : IReplayViewPlugin
{
    public string Id => "builtin.fps";
    public int Order => 100;
    public bool CanCreate(ReplayPluginContext context) => Select(context).Any(stage => stage.Fps is { Count: > 0 });
    public IReadOnlyList<ReplayView> Create(ReplayPluginContext context)
    {
        var selected = Select(context).Where(stage => stage.Fps is { Count: > 0 }).ToArray();
        var boundaries = new List<double>();
        var elapsedFrames = 0;
        var points = new List<ReplayViewPoint>();
        foreach (var stage in selected)
        {
            var interval = stage.FpsIntervalFrames ?? 30;
            var fps = stage.Fps!;
            points.AddRange(fps.Select((value, index) => new ReplayViewPoint(elapsedFrames + index * interval, value)));
            elapsedFrames += fps.Count * interval;
            boundaries.Add(elapsedFrames);
        }
        if (boundaries.Count > 0) boundaries.RemoveAt(boundaries.Count - 1);
        return [new ReplayView("fps-chart", "view.fpsChart", ReplayViewKind.LineChart,
            Series: [new ReplayViewSeries("fps", context.Presentation.Text("view.fps"), points)],
            Metadata: new Dictionary<string, object?>
            {
                ["xUnit"] = context.Presentation.Text("column.timeSeconds"), ["yUnit"] = "FPS", ["xIsFrame"] = true,
                ["stageBoundaries"] = boundaries.ToArray(),
                ["stageLabels"] = selected.Select(stage => $"{context.Presentation.Text("section.stage")} {stage.StageId}").ToArray(),
                ["interactionHint"] = context.Presentation.Text("chart.interactionHint"),
                ["averageLabel"] = context.Presentation.Text("chart.average"), ["showAnchors"] = true
            })];
    }

    private static IEnumerable<ReplayStage> Select(ReplayPluginContext context) => context.StageIndex is { } index ? [context.Replay.Stages[index]] : context.Replay.Stages;
}

internal sealed class FpsListPlugin : IReplayViewPlugin
{
    public string Id => "builtin.fps-list";
    public int Order => 80;
    public bool CanCreate(ReplayPluginContext context) => context.StageIndex is { } index && context.Replay.Stages[index].Fps is { Count: > 0 };
    public IReadOnlyList<ReplayView> Create(ReplayPluginContext context)
    {
        var stage = context.Replay.Stages[context.StageIndex!.Value];
        var interval = stage.FpsIntervalFrames ?? 30;
        var rows = stage.Fps!.Select((fps, index) => new ReplayViewRow(index.ToString(),
            [index, index * interval, ReplayFrameTime.Format(index * interval), fps, stage.RawFps?[index] ?? fps])).ToArray();
        return [new ReplayView("fps-list", "view.fpsList", ReplayViewKind.Table,
            [new("sample", context.Presentation.Text("column.sample")), new("frame", context.Presentation.Text("column.frame")),
             new("time", context.Presentation.Text("column.timeSeconds")), new("fps", "FPS"), new("raw", context.Presentation.Text("column.raw"))], rows)];
    }
}

internal sealed class KeyListPlugin : IReplayViewPlugin
{
    public string Id => "builtin.key-list";
    public int Order => 60;
    public bool CanCreate(ReplayPluginContext context) => context.StageIndex is { } index && context.Replay.Stages[index].Keys.Count > 0;
    public IReadOnlyList<ReplayView> Create(ReplayPluginContext context)
    {
        var stage = context.Replay.Stages[context.StageIndex!.Value];
        var rows = stage.Keys.Select((key, frame) =>
        {
            var raw = unchecked((ushort)stage.RawKeys[frame]);
            var unknown = (ushort)(raw & ~KnownRawMask(context.Replay.Identity.FormatId));
            var suffix = unknown == 0 ? "" : $"(0x{raw:X4})";
            var direction = ((int)key & 0xf) == 0 ? "" : context.Presentation.Text($"direction.{(int)key & 0xf}");
            var actions = Actions(key);
            if (suffix.Length > 0)
            {
                if (actions.Length > 0) actions += suffix;
                else direction += suffix;
            }
            return new ReplayViewRow(frame.ToString(), [frame, ReplayFrameTime.Format(frame), direction, actions, stage.RawKeys[frame]]);
        }).ToArray();
        return [new ReplayView("key-list", "view.keyList", ReplayViewKind.Table,
            [new("frame", context.Presentation.Text("column.frame")), new("time", context.Presentation.Text("column.timeSeconds")),
             new("direction", context.Presentation.Text("column.direction")), new("actions", context.Presentation.Text("column.actions")),
             new("raw", context.Presentation.Text("column.raw"))], rows)];
    }

    private static string Actions(ReplayKey key)
    {
        var values = new List<string>();
        var labels = new[]
        {
            (ReplayKey.Z, "Z"), (ReplayKey.X, "X"), (ReplayKey.C, "C"),
            (ReplayKey.D, "D"), (ReplayKey.Shift, "Δ"), (ReplayKey.Ctrl, "Σ"), (ReplayKey.V, "V")
        };
        foreach (var (flag, label) in labels)
            if ((key & flag) != 0) values.Add(label);
        return string.Concat(values);
    }

    private static ushort KnownRawMask(string formatId)
    {
        var canonical = formatId.EndsWith("Trial", StringComparison.OrdinalIgnoreCase) ? formatId[..^5] : formatId;
        if (canonical.Equals("alcostg", StringComparison.OrdinalIgnoreCase)) return 0x02ff;
        return canonical switch
        {
            "TH06" or "TH07" or "TH08" or "TH09" or "TH10" => 0x01f7,
            "TH09.5" => 0x01f7,
            "TH12.5" => 0x00ff,
            "TH13" or "TH14" or "TH14.3" or "TH15" or "TH16" or "TH16.5" => 0x0afb,
            "TH18" => 0x0cfb,
            "TH20" => 0x00fd,
            _ => 0x02fb
        };
    }
}

internal sealed class KeyTransitionPlugin : IReplayViewPlugin
{
    public string Id => "builtin.keys";
    public int Order => 200;
    public bool CanCreate(ReplayPluginContext context) => context.Replay.Stages.Any(stage => stage.Keys.Count > 1);
    public IReadOnlyList<ReplayView> Create(ReplayPluginContext context)
    {
        var statistics = context.StageIndex is { } index ? context.Replay.Stages[index].Statistics : Aggregate(context.Replay);
        var directions = Enumerable.Range(0, 16).Select(index => context.Presentation.Text($"direction.{index}")).ToArray();
        var columns = new[] { new ReplayViewColumn("from", context.Presentation.Text("column.from")) }
            .Concat(directions.Select((name, index) => new ReplayViewColumn(index.ToString(), name))).ToArray();
        var rows = Enumerable.Range(0, 16).Select(from => new ReplayViewRow(from.ToString(), new object?[] { directions[from] }.Concat(statistics.Matrix[from].Cast<object?>()).ToArray())).ToArray();
        var selectedStages = context.StageIndex is { } selected ? [context.Replay.Stages[selected]] : context.Replay.Stages.ToArray();
        var boundaries = new List<int>(); var total = 0;
        foreach (var stage in selectedStages) { total += stage.Keys.Count; boundaries.Add(total); }
        if (boundaries.Count > 0) boundaries.RemoveAt(boundaries.Count - 1);
        return [new ReplayView("key-transitions", "view.keyTransitions", ReplayViewKind.HeatMap, columns, rows,
            Metadata: new Dictionary<string, object?> { ["frames"] = statistics.Frames, ["stageBoundaries"] = boundaries.ToArray(), ["includesSelfTransitions"] = true, ["sampleIntervalFrames"] = 1 })];
    }

    private static ReplayStatistics Aggregate(ReplayDocument replay)
    {
        var matrix = ReplayStatistics.CreateMatrix();
        var frames = ReplayStatistics.CreateFrameMatrix();
        var frameBase = 0;
        foreach (var stage in replay.Stages)
        {
            var source = stage.Statistics;
            for (var from = 0; from < 16; from++)
                for (var to = 0; to < 16; to++)
                {
                    matrix[from][to] += source.Matrix[from][to];
                    frames[from][to].AddRange(source.Frames[from][to].Select(frame => frameBase + frame));
                }
            frameBase += stage.Keys.Count;
        }
        return new ReplayStatistics { Matrix = matrix, Frames = frames };
    }
}

internal sealed class KeyRatePlugin : IReplayViewPlugin
{
    public string Id => "builtin.key-rates";
    public int Order => 180;
    public bool CanCreate(ReplayPluginContext context) => Select(context).Any(stage => stage.Keys.Count > 1);
    public IReadOnlyList<ReplayView> Create(ReplayPluginContext context)
    {
        var aps = new List<ReplayViewPoint>(); var dps = new List<ReplayViewPoint>(); var dfps = new List<ReplayViewPoint>();
        var boundaries = new List<double>(); var frameBase = 0; var punishment = context.Punishment ?? DirectionPunishmentTable.CreateDefault();
        foreach (var stage in Select(context))
        {
            var rates = ReplayKeyRateStatistics.Analyze(context.Replay.Identity.FormatId, stage.Keys, punishment);
            aps.AddRange(rates.Aps.Select((value, frame) => new ReplayViewPoint(frameBase + frame, value)));
            dps.AddRange(rates.Dps.Select((value, frame) => new ReplayViewPoint(frameBase + frame, value)));
            dfps.AddRange(rates.Dfps.Select((value, frame) => new ReplayViewPoint(frameBase + frame, value)));
            frameBase += stage.Keys.Count; boundaries.Add(frameBase);
        }
        if (boundaries.Count > 0) boundaries.RemoveAt(boundaries.Count - 1);
        return [new ReplayView("key-rates", "view.keyRates", ReplayViewKind.LineChart, Series:
            [new("aps", "APS", aps), new("dps", "DPS", dps), new("dfps", "DFPS", dfps)], Metadata: new Dictionary<string, object?>
            {
                ["xUnit"] = context.Presentation.Text("column.timeSeconds"), ["yUnit"] = context.Presentation.Text("unit.rate"), ["xIsFrame"] = true,
                ["stageBoundaries"] = boundaries.ToArray(), ["averageLabel"] = context.Presentation.Text("chart.average"),
                ["interactionHint"] = context.Presentation.Text("chart.interactionHint")
            })];
    }
    private static IEnumerable<ReplayStage> Select(ReplayPluginContext context) => context.StageIndex is { } index ? [context.Replay.Stages[index]] : context.Replay.Stages;
}
