using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace RepViewer.App;

internal sealed record FileAssociationStatus(bool IsAssociated, bool MatchesCurrentPath);

internal static class FileAssociationService
{
    private const string ProgId = "RepViewer.Replay";
    private const string ClassesPath = @"Software\Classes";
    private const string SettingsPath = @"Software\RepViewer";
    private const string EnabledValue = "FileAssociationEnabled";
    private const string InstallDirectoryValue = "AssociatedInstallDirectory";
    private const string ShellHostValue = "AssociatedShellHost";
    private const string SuppressValue = "SuppressFileAssociationPrompt";
    private const string SourcePathValue = "RepViewerSourcePath";
    private const string ThumbnailSlot = "{E357FCCD-A995-4576-B01F-234630154E96}";
    private const string IconClassId = "B2590446-F6A4-4A21-A179-3CF83547FE21";
    private const string ThumbnailClassId = "50B06B83-4104-43B3-BF54-E7849D8AD145";
    private static string ShellArchitecture => Environment.Is64BitOperatingSystem ? "x64" : "x86";
    private static string ShellBaseName => $"RepViewer.Shell.{ShellArchitecture}";
    private static string[] ShellFiles =>
    [
        $"{ShellBaseName}.comhost.dll", $"{ShellBaseName}.dll", $"{ShellBaseName}.deps.json",
        $"{ShellBaseName}.runtimeconfig.json", "RepViewer.Core.dll"
    ];
    private static readonly string[] OwnedIconHandlers =
    [
        IconClassId, "8C9958C7-A9BF-4B11-87C1-33DFD848D06B", "23E70D1B-F04E-45D5-BAB7-C8A64E0B45F9", "DAAD97F0-C40C-4C6E-AEAC-A2639A020FEA", "A0434666-FB99-46F8-9282-AEEC1D17FCE0",
        "75342BB5-935B-479E-8275-F4A96D90F47B"
    ];
    private static readonly string[] OwnedThumbnailHandlers =
    [
        ThumbnailClassId, "0BC131AF-92B6-4DF0-AC66-D9EA5324CF59", "C43D4E2A-6154-4B6D-A23A-5FE2BB809D57", "69764687-9204-4CDA-89A0-93C59A329B64", "A180237C-9F07-4565-965F-F45286214605", "FF45BF57-5E3D-442B-83DB-1A9783DD3AE3",
        "D65CC3A8-C942-45B7-B89D-1575F4EE8291"
    ];

    private static RegistryKey CurrentUser => RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,
        Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default);

    public static bool PromptSuppressed
    {
        get { using var root = CurrentUser; using var key = root.OpenSubKey(SettingsPath); return key?.GetValue(SuppressValue) is int value && value != 0; }
        set { using var root = CurrentUser; using var key = root.CreateSubKey(SettingsPath, true); key.SetValue(SuppressValue, value ? 1 : 0, RegistryValueKind.DWord); }
    }

    public static FileAssociationStatus GetStatus()
    {
        using var root = CurrentUser;
        var installDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        var expectedHost = TryGetDeploymentPath(AppContext.BaseDirectory);
        using (var settings = root.OpenSubKey(SettingsPath))
        {
            if (settings?.GetValue(EnabledValue) is int enabled)
            {
                if (enabled == 0) return new(false, false);
                var savedDirectory = settings.GetValue(InstallDirectoryValue) as string;
                var savedHost = settings.GetValue(ShellHostValue) as string;
                return new(true, PathEquals(savedDirectory, installDirectory) && expectedHost is not null && PathEquals(savedHost, expectedHost));
            }
        }
        var extensionValue = ReadDefault(root, $@"{ClassesPath}\.rpy");
        var userChoice = ReadNamed(root, @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.rpy\UserChoice", "ProgId");
        var effectiveProgId = string.IsNullOrWhiteSpace(userChoice) ? extensionValue : userChoice;
        var effectiveCommand = string.IsNullOrWhiteSpace(effectiveProgId) ? null : ReadDefault(root, $@"{ClassesPath}\{effectiveProgId}\shell\open\command");
        var iconHandler = ReadDefault(root, $@"{ClassesPath}\.rpy\shellex\IconHandler");
        var thumbnailHandler = ReadDefault(root, $@"{ClassesPath}\.rpy\shellex\{ThumbnailSlot}");
        var associated = (string.Equals(effectiveProgId, ProgId, StringComparison.OrdinalIgnoreCase) || CommandTargetsInstall(effectiveCommand, installDirectory))
            && IsClass(iconHandler, IconClassId) && IsClass(thumbnailHandler, ThumbnailClassId);
        if (!associated) return new(false, false);

        using var progId = root.OpenSubKey($@"{ClassesPath}\{ProgId}");
        var source = progId?.GetValue(SourcePathValue) as string;
        var command = ReadDefault(root, $@"{ClassesPath}\{ProgId}\shell\open\command");
        var iconHost = ReadDefault(root, $@"{ClassesPath}\CLSID\{{{IconClassId}}}\InprocServer32");
        var thumbnailHost = ReadDefault(root, $@"{ClassesPath}\CLSID\{{{ThumbnailClassId}}}\InprocServer32");
        var pathMatches = PathEquals(source, installDirectory) && CommandTargetsInstall(command, installDirectory)
            && (string.Equals(effectiveProgId, ProgId, StringComparison.OrdinalIgnoreCase) || CommandTargetsInstall(effectiveCommand, installDirectory))
            && expectedHost is not null && PathEquals(iconHost, expectedHost) && PathEquals(thumbnailHost, expectedHost);
        return new(true, pathMatches);
    }

    public static void EnsureCurrentRegistration()
    {
        var status = GetStatus();
        if (!status.IsAssociated || !status.MatchesCurrentPath) return;
        using var root = CurrentUser;
        var installDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        var expectedHost = TryGetDeploymentPath(AppContext.BaseDirectory);
        var valid = string.Equals(ReadDefault(root, $@"{ClassesPath}\.rpy"), ProgId, StringComparison.OrdinalIgnoreCase)
            && IsClass(ReadDefault(root, $@"{ClassesPath}\.rpy\shellex\IconHandler"), IconClassId)
            && IsClass(ReadDefault(root, $@"{ClassesPath}\.rpy\shellex\{ThumbnailSlot}"), ThumbnailClassId)
            && CommandTargetsInstall(ReadDefault(root, $@"{ClassesPath}\{ProgId}\shell\open\command"), installDirectory)
            && expectedHost is not null
            && PathEquals(ReadDefault(root, $@"{ClassesPath}\CLSID\{{{IconClassId}}}\InprocServer32"), expectedHost)
            && PathEquals(ReadDefault(root, $@"{ClassesPath}\CLSID\{{{ThumbnailClassId}}}\InprocServer32"), expectedHost);
        if (!valid) AssociateCurrent(refreshExplorer: false);
    }

    public static void AssociateCurrent(bool refreshExplorer)
    {
        var executable = CurrentExecutable();
        var installDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        var comHost = DeployShell(AppContext.BaseDirectory);
        using var root = CurrentUser;
        SetDefault(root, $@"{ClassesPath}\.rpy", ProgId);
        SetNamed(root, $@"{ClassesPath}\.rpy\OpenWithProgids", ProgId, "");
        SetDefault(root, $@"{ClassesPath}\.rpy\shellex\IconHandler", $"{{{IconClassId}}}");
        SetDefault(root, $@"{ClassesPath}\.rpy\shellex\{ThumbnailSlot}", $"{{{ThumbnailClassId}}}");
        SetDefault(root, $@"{ClassesPath}\{ProgId}", "东方 Replay 文件");
        SetNamed(root, $@"{ClassesPath}\{ProgId}", SourcePathValue, installDirectory);
        SetNamed(root, $@"{ClassesPath}\{ProgId}", "TypeOverlay", "");
        SetDefault(root, $@"{ClassesPath}\{ProgId}\DefaultIcon", $"\"{executable}\",0");
        SetDefault(root, $@"{ClassesPath}\{ProgId}\shell\open\command", $"\"{executable}\" \"%1\"");
        SetDefault(root, $@"{ClassesPath}\{ProgId}\shellex\IconHandler", $"{{{IconClassId}}}");
        SetDefault(root, $@"{ClassesPath}\{ProgId}\shellex\{ThumbnailSlot}", $"{{{ThumbnailClassId}}}");
        var applicationPath = $@"{ClassesPath}\Applications\{Path.GetFileName(executable)}";
        SetDefault(root, $@"{applicationPath}\shell\open\command", $"\"{executable}\" \"%1\"");
        SetNamed(root, $@"{applicationPath}\SupportedTypes", ".rpy", "");
        RegisterClass(root, IconClassId, comHost, "RepViewer replay icon handler");
        RegisterClass(root, ThumbnailClassId, comHost, "RepViewer replay thumbnail provider");
        foreach (var obsoleteClassId in OwnedIconHandlers.Where(classId => !classId.Equals(IconClassId, StringComparison.OrdinalIgnoreCase))
                     .Concat(OwnedThumbnailHandlers.Where(classId => !classId.Equals(ThumbnailClassId, StringComparison.OrdinalIgnoreCase))))
            root.DeleteSubKeyTree($@"{ClassesPath}\CLSID\{{{obsoleteClassId}}}", false);
        SetAssociationState(root, true, installDirectory, comHost, false);
        if (refreshExplorer) RefreshExplorerCaches();
        else NotifyShell();
    }

    public static void Unassociate(bool suppressPrompt, bool refreshExplorer, bool notifyShell = true)
    {
        using var root = CurrentUser;
        var shellChanged = HasOwnedRegistration(root);
        using (var extension = root.OpenSubKey($@"{ClassesPath}\.rpy", true))
            if (string.Equals(extension?.GetValue(null) as string, ProgId, StringComparison.OrdinalIgnoreCase)) extension!.DeleteValue(null!, false);
        DeleteOwnedHandler(root, $@"{ClassesPath}\.rpy\shellex\IconHandler", OwnedIconHandlers);
        DeleteOwnedHandler(root, $@"{ClassesPath}\.rpy\shellex\{ThumbnailSlot}", OwnedThumbnailHandlers);
        using (var openWith = root.OpenSubKey($@"{ClassesPath}\.rpy\OpenWithProgids", true)) openWith?.DeleteValue(ProgId, false);
        root.DeleteSubKeyTree($@"{ClassesPath}\{ProgId}", false);
        root.DeleteSubKeyTree($@"{ClassesPath}\Applications\RepViewer.exe", false);
        root.DeleteSubKeyTree($@"{ClassesPath}\Applications\RepViewer.x64.exe", false);
        foreach (var classId in OwnedIconHandlers.Concat(OwnedThumbnailHandlers))
            root.DeleteSubKeyTree($@"{ClassesPath}\CLSID\{{{classId}}}", false);
        SetAssociationState(root, false, null, null, suppressPrompt);
        if (!shellChanged || !notifyShell) return;
        if (refreshExplorer) RefreshExplorerCaches();
        else NotifyShell();
    }

    private static bool HasOwnedRegistration(RegistryKey root)
    {
        if (string.Equals(ReadDefault(root, $@"{ClassesPath}\.rpy"), ProgId, StringComparison.OrdinalIgnoreCase)) return true;
        if (OwnedIconHandlers.Any(classId => IsClass(ReadDefault(root, $@"{ClassesPath}\.rpy\shellex\IconHandler"), classId))) return true;
        if (OwnedThumbnailHandlers.Any(classId => IsClass(ReadDefault(root, $@"{ClassesPath}\.rpy\shellex\{ThumbnailSlot}"), classId))) return true;
        using (var openWith = root.OpenSubKey($@"{ClassesPath}\.rpy\OpenWithProgids"))
            if (openWith?.GetValueNames().Contains(ProgId, StringComparer.OrdinalIgnoreCase) == true) return true;
        if (KeyExists(root, $@"{ClassesPath}\{ProgId}") ||
            KeyExists(root, $@"{ClassesPath}\Applications\RepViewer.exe") ||
            KeyExists(root, $@"{ClassesPath}\Applications\RepViewer.x64.exe")) return true;
        return OwnedIconHandlers.Concat(OwnedThumbnailHandlers).Any(classId => KeyExists(root, $@"{ClassesPath}\CLSID\{{{classId}}}"));
    }

    private static string DeployShell(string sourceDirectory)
    {
        var destination = TryGetDeploymentDirectory(sourceDirectory)
            ?? throw new FileNotFoundException("缺少 Explorer 扩展组件，请重新发布完整 portable 版本。");
        Directory.CreateDirectory(destination);
        foreach (var file in ShellFiles) CopyIfChanged(Path.Combine(sourceDirectory, file), Path.Combine(destination, file));
        var sourceAssets = Path.Combine(sourceDirectory, "icons", "thumbnail");
        var destinationAssets = Path.Combine(destination, "icons", "thumbnail");
        Directory.CreateDirectory(destinationAssets);
        foreach (var asset in Directory.EnumerateFiles(sourceAssets, "*.png"))
            CopyIfChanged(asset, Path.Combine(destinationAssets, Path.GetFileName(asset)));
        return Path.Combine(destination, $"{ShellBaseName}.comhost.dll");
    }

    private static string? TryGetDeploymentPath(string sourceDirectory)
        => TryGetDeploymentDirectory(sourceDirectory) is { } directory ? Path.Combine(directory, $"{ShellBaseName}.comhost.dll") : null;

    private static string? TryGetDeploymentDirectory(string sourceDirectory)
    {
        if (ShellFiles.Any(file => !File.Exists(Path.Combine(sourceDirectory, file)))) return null;
        var sourceAssets = Path.Combine(sourceDirectory, "icons", "thumbnail");
        if (!Directory.Exists(sourceAssets)) return null;
        var assets = Directory.EnumerateFiles(sourceAssets, "*.png").OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToArray();
        if (assets.Length == 0) return null;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in ShellFiles) AppendFile(hash, file, Path.Combine(sourceDirectory, file));
        foreach (var asset in assets) AppendFile(hash, Path.GetFileName(asset), asset);
        var version = Convert.ToHexString(hash.GetHashAndReset())[..16];
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RepViewer", "shell", version);
    }

    private static void AppendFile(IncrementalHash hash, string name, string path)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(name));
        hash.AppendData(File.ReadAllBytes(path));
    }

    private static void CopyIfChanged(string source, string destination)
    {
        if (File.Exists(destination) && new FileInfo(source).Length == new FileInfo(destination).Length
            && File.ReadAllBytes(source).AsSpan().SequenceEqual(File.ReadAllBytes(destination))) return;
        File.Copy(source, destination, true);
    }

    private static void RegisterClass(RegistryKey root, string classId, string comHost, string description)
    {
        var path = $@"{ClassesPath}\CLSID\{{{classId}}}";
        SetDefault(root, path, description); SetDefault(root, $@"{path}\InprocServer32", comHost);
        SetNamed(root, $@"{path}\InprocServer32", "ThreadingModel", "Apartment");
    }

    private static void SetAssociationState(RegistryKey root, bool enabled, string? installDirectory, string? shellHost, bool suppressPrompt)
    {
        using var settings = root.CreateSubKey(SettingsPath, true);
        settings.SetValue(EnabledValue, enabled ? 1 : 0, RegistryValueKind.DWord);
        settings.SetValue(SuppressValue, suppressPrompt ? 1 : 0, RegistryValueKind.DWord);
        if (installDirectory is null) settings.DeleteValue(InstallDirectoryValue, false); else settings.SetValue(InstallDirectoryValue, installDirectory, RegistryValueKind.String);
        if (shellHost is null) settings.DeleteValue(ShellHostValue, false); else settings.SetValue(ShellHostValue, shellHost, RegistryValueKind.String);
    }

    private static string? ReadDefault(RegistryKey root, string path) { using var key = root.OpenSubKey(path); return key?.GetValue(null) as string; }
    private static string? ReadNamed(RegistryKey root, string path, string name) { using var key = root.OpenSubKey(path); return key?.GetValue(name) as string; }
    private static bool KeyExists(RegistryKey root, string path) { using var key = root.OpenSubKey(path); return key is not null; }
    private static void SetDefault(RegistryKey root, string path, string value) { using var key = root.CreateSubKey(path, true); key.SetValue(null, value, RegistryValueKind.String); }
    private static void SetNamed(RegistryKey root, string path, string name, string value) { using var key = root.CreateSubKey(path, true); key.SetValue(name, value, RegistryValueKind.String); }
    private static bool IsClass(string? value, string classId) => string.Equals(value, $"{{{classId}}}", StringComparison.OrdinalIgnoreCase);
    private static bool PathEquals(string? left, string? right) => !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right)
        && string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);
    private static bool CommandTargetsInstall(string? command, string installDirectory)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        var trimmed = command.TrimStart();
        var executable = trimmed.StartsWith('"') ? trimmed[1..].Split('"', 2)[0] : trimmed.Split(' ', 2)[0];
        try
        {
            var name = Path.GetFileName(executable);
            return (name.Equals("RepViewer.exe", StringComparison.OrdinalIgnoreCase) || name.Equals("RepViewer.x64.exe", StringComparison.OrdinalIgnoreCase))
                && PathEquals(Path.GetDirectoryName(Path.GetFullPath(executable)), installDirectory);
        }
        catch { return false; }
    }
    private static string CurrentExecutable() => Path.GetFullPath(Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName
        ?? throw new InvalidOperationException("无法确定 RepViewer 的程序路径。"));

    private static void DeleteOwnedHandler(RegistryKey root, string path, IReadOnlyCollection<string> owned)
    {
        using var key = root.OpenSubKey(path);
        var value = key?.GetValue(null) as string;
        if (!owned.Any(classId => IsClass(value, classId))) return;
        key!.Close(); root.DeleteSubKeyTree(path, false);
    }

    private static void RefreshExplorerCaches()
    {
        var sessionId = Process.GetCurrentProcess().SessionId;
        var shellProcesses = Process.GetProcessesByName("explorer").Where(process => process.SessionId == sessionId).ToList();
        foreach (var process in Process.GetProcessesByName("dllhost"))
        {
            if (process.SessionId == sessionId && HasLoadedReplayShell(process)) shellProcesses.Add(process);
            else process.Dispose();
        }
        foreach (var process in shellProcesses)
        {
            try { process.Kill(); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }
        foreach (var process in shellProcesses)
        {
            try { process.WaitForExit(5000); }
            catch (InvalidOperationException) { }
            finally { process.Dispose(); }
        }

        var cacheDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Explorer");
        if (Directory.Exists(cacheDirectory))
        {
            var cacheFiles = Directory.EnumerateFiles(cacheDirectory, "*.db").Where(file =>
            {
                var name = Path.GetFileName(file);
                return name.StartsWith("thumbcache_", StringComparison.OrdinalIgnoreCase) || name.StartsWith("iconcache_", StringComparison.OrdinalIgnoreCase);
            }).ToArray();
            for (var attempt = 0; attempt < 20 && cacheFiles.Any(File.Exists); attempt++)
            {
                foreach (var file in cacheFiles.Where(File.Exists))
                {
                    try { File.Delete(file); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                if (cacheFiles.Any(File.Exists)) Thread.Sleep(250);
            }
        }
        var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        try { Process.Start(new ProcessStartInfo(explorer) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { }
        NotifyShell();
    }

    private static bool HasLoadedReplayShell(Process process)
    {
        try
        {
            return process.Modules.Cast<ProcessModule>().Any(module =>
                module.ModuleName.StartsWith("RepViewer.Shell.", StringComparison.OrdinalIgnoreCase) &&
                module.ModuleName.EndsWith(".comhost.dll", StringComparison.OrdinalIgnoreCase));
        }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    private static void NotifyShell() => SHChangeNotify(0x08000000, 0, 0, 0);
    [DllImport("shell32.dll")] private static extern void SHChangeNotify(uint eventId, uint flags, nint item1, nint item2);
}
