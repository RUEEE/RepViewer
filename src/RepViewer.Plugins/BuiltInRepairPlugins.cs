using RepViewer.Core;

namespace RepViewer.Plugins;

/// <summary>
/// Surfaces the known TH07/08/09 localized-executable incompatibility. Applying remains disabled
/// until the inverse compressor/encrypter can round-trip verified samples byte-for-byte.
/// </summary>
internal sealed class LocalizedExecutableCompatibilityPlugin : IReplayRepairPlugin
{
    public string Id => "builtin.localized-executable";

    public IReadOnlyList<ReplayRepairFinding> Analyze(ReplayPluginContext context)
    {
        if (!context.Replay.Issues.Any(issue => issue.Code == "localized-executable-checksum")) return [];
        return [new ReplayRepairFinding(Id, "restore-original-executable-signature", "repair.localizedExecutable.title",
            "repair.localizedExecutable.unavailable", ReplayIssueSeverity.Warning, ReplayRepairRisk.Caution, false)];
    }

    public ReplayRepairResult Apply(ReplayPluginContext context, ReplayRepairFinding finding) =>
        throw new NotSupportedException("The replay compressor/encrypter writer is not implemented yet.");
}
