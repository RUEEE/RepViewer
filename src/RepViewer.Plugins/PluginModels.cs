using RepViewer.Core;
using RepViewer.Presentation;

namespace RepViewer.Plugins;

public enum ReplayViewKind { Properties, Table, LineChart, HeatMap }
public sealed record ReplayViewColumn(string Id, string Label);
public sealed record ReplayViewPoint(double X, double Y);
public sealed record ReplayViewSeries(string Id, string Label, IReadOnlyList<ReplayViewPoint> Points);
public sealed record ReplayViewRow(string Id, IReadOnlyList<object?> Cells);

/// <summary>Renderer-neutral plugin output. WPF, exporters and future frontends consume the same model.</summary>
public sealed record ReplayView(
    string Id,
    string Title,
    ReplayViewKind Kind,
    IReadOnlyList<ReplayViewColumn>? Columns = null,
    IReadOnlyList<ReplayViewRow>? Rows = null,
    IReadOnlyList<ReplayViewSeries>? Series = null,
    IReadOnlyDictionary<string, object?>? Metadata = null);

public sealed record ReplayPluginContext(ReplayDocument Replay, PresentationCatalog Presentation, int? StageIndex = null, DirectionPunishmentTable? Punishment = null);

public interface IReplayViewPlugin
{
    string Id { get; }
    int Order { get; }
    bool CanCreate(ReplayPluginContext context);
    IReadOnlyList<ReplayView> Create(ReplayPluginContext context);
}

public sealed class ReplayPluginHost
{
    private readonly List<IReplayViewPlugin> _plugins = [];
    public ReplayPluginHost(IEnumerable<IReplayViewPlugin>? plugins = null) => _plugins.AddRange(plugins ?? BuiltInReplayPlugins.All);
    public IReadOnlyList<IReplayViewPlugin> Plugins => _plugins;
    public IReadOnlyList<ReplayView> CreateViews(ReplayPluginContext context) =>
        _plugins.Where(plugin => plugin.CanCreate(context)).OrderBy(plugin => plugin.Order).SelectMany(plugin => plugin.Create(context)).ToArray();
}

public enum ReplayRepairRisk { Safe, Caution, Destructive }

/// <summary>A repair plugin may diagnose without being able to apply a repair.</summary>
public sealed record ReplayRepairFinding(
    string PluginId,
    string Id,
    string Title,
    string Description,
    ReplayIssueSeverity Severity,
    ReplayRepairRisk Risk,
    bool CanApply);

public sealed record ReplayRepairResult(byte[] Bytes, string Summary, IReadOnlyList<ReplayIssue> RemainingIssues);

public interface IReplayRepairPlugin
{
    string Id { get; }
    IReadOnlyList<ReplayRepairFinding> Analyze(ReplayPluginContext context);
    ReplayRepairResult Apply(ReplayPluginContext context, ReplayRepairFinding finding);
}

/// <summary>
/// Repair execution is intentionally separate from view generation. The host only returns bytes;
/// the App owns confirmation, destination selection, writing and reparsing verification.
/// </summary>
public sealed class ReplayRepairPluginHost
{
    private readonly IReadOnlyList<IReplayRepairPlugin> _plugins;
    public ReplayRepairPluginHost(IEnumerable<IReplayRepairPlugin>? plugins = null) => _plugins = (plugins ?? BuiltInReplayRepairPlugins.All).ToArray();
    public IReadOnlyList<ReplayRepairFinding> Analyze(ReplayPluginContext context) => _plugins.SelectMany(plugin => plugin.Analyze(context)).ToArray();
    public ReplayRepairResult Apply(ReplayPluginContext context, ReplayRepairFinding finding)
    {
        var plugin = _plugins.SingleOrDefault(candidate => candidate.Id == finding.PluginId) ?? throw new InvalidOperationException($"Repair plugin not found: {finding.PluginId}");
        if (!finding.CanApply) throw new NotSupportedException(finding.Description);
        return plugin.Apply(context, finding);
    }
}

public static class BuiltInReplayPlugins
{
    public static IReadOnlyList<IReplayViewPlugin> All { get; } = [new OverviewPlugin(), new KeyListPlugin(), new FpsListPlugin(), new FpsPlugin(), new KeyRatePlugin(), new KeyTransitionPlugin()];
}

public static class BuiltInReplayRepairPlugins
{
    public static IReadOnlyList<IReplayRepairPlugin> All { get; } = [new LocalizedExecutableCompatibilityPlugin()];
}
