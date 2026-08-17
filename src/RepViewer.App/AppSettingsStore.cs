using System.IO;
using System.Text.Json;

namespace RepViewer.App;

internal sealed class AppSettings
{
    public string? Locale { get; set; }
    public bool ChartUseScientificNotation { get; set; }
    public bool ChartUseThousandsSeparator { get; set; } = true;
    public int UiScalePercent { get; set; } = 100;
    public string[]? EnabledPluginIds { get; set; }
}

internal static class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RepViewer", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            return File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions) ?? new AppSettings()
                : new AppSettings();
        }
        catch { return new AppSettings(); }
    }

    public static void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
