using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RepViewer.Core;

namespace RepViewer.Shell;

internal sealed record ShellMetadata(string Game, string Name, string DifficultyAsset, string? CharacterAsset, string? ShotTypeLabel, string? SpecialAsset, string? LowerLeftLabel)
{
    public static ShellMetadata Empty { get; } = new("default", "", "", null, null, null, null);
    public static ShellMetadata From(ReplayDocument replay)
    {
        ReplayPropertyNode? Node(string name) => replay.General.Children.FirstOrDefault(node => node.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        object? Field(string name) => Node(name)?.SemanticValue;
        int Number(string name, int fallback = -1) { try { return Convert.ToInt32(Field(name), System.Globalization.CultureInfo.InvariantCulture); } catch { return fallback; } }
        int RawNumber(string name, int fallback = -1) { try { return Convert.ToInt32(Node(name)?.RawValue, System.Globalization.CultureInfo.InvariantCulture); } catch { return fallback; } }
        var game = replay.Identity.GameId.ToLowerInvariant();
        var name = Field("Name") switch
        {
            byte[] bytes => ReplayTextEncoding.ShiftJis.GetString(bytes).TrimEnd('\0'),
            { } value => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "",
            _ => ""
        };
        if (name.Length > 8) name = name[..8];
        var rank = RawNumber("Rank");
        var difficulty = rank switch { 0 => "Diff_E.png", 1 => "Diff_N.png", 2 => "Diff_H.png", 3 => "Diff_L.png", 4 => "Diff_Ex.png", 5 when game == "th07" => "Diff_Ph.png", 5 when game == "th13" => "Diff_Od.png", _ => "" };
        var characterValue = RawNumber("Character");
        var character = Character(game, characterValue);
        var shot = RawNumber("ShotType");
        string? label = game switch
        {
            "th10" or "th11" when shot is >= 0 and <= 2 => ((char)('A' + shot)).ToString(),
            "th12" or "th14" when shot is >= 0 and <= 1 => ((char)('A' + shot)).ToString(),
            _ => null
        };
        string? special = null; string? lowerLeft = null;
        if (game is "th06" or "th07")
        {
            character = Character(game, characterValue / 2);
            label = characterValue >= 0 ? ((char)('A' + characterValue % 2)).ToString() : null;
        }
        else if (game == "th08") label = Th08Route(replay);
        else if (game == "th095") { character = "Aya.png"; lowerLeft = $"{Number("LevelId")}-{Number("SubLevelId")}"; label = null; difficulty = ""; }
        else if (game == "th125")
        {
            var levelId = Number("LevelId");
            lowerLeft = $"{(levelId == 13 ? "EX" : levelId.ToString(System.Globalization.CultureInfo.InvariantCulture))}-{Number("SubLevelId")}";
            label = null; difficulty = "";
        }
        else if (game == "th128") { character = "Cirno.png"; label = Route(RawNumber("Route")); }
        else if (game == "th143") { character = "Seija.png"; lowerLeft = $"{shot + 1}-{rank + 1}"; label = null; difficulty = ""; }
        else if (game == "th165") { character = "Usami.png"; lowerLeft = $"{rank + 1}-{shot + 1}"; label = null; }
        else if (game == "th16") special = new[] { "Season_Spring.png", "Season_Summer.png", "Season_Autumn.png", "Season_Winter.png", "Season_Full.png" }.ElementAtOrDefault(shot);
        else if (game == "th17") special = new[] { "Ghost_Wolf.png", "Ghost_Otter.png", "Ghost_Eagle.png" }.ElementAtOrDefault(shot);
        else if (game == "th20") special = new[] { "Stone_R1.png", "Stone_R2.png", "Stone_B1.png", "Stone_B2.png", "Stone_Y1.png", "Stone_Y2.png", "Stone_G1.png", "Stone_G2.png" }.ElementAtOrDefault(Number("ShotType1"));
        return new ShellMetadata(game, name, difficulty, character, label, special, lowerLeft);
    }

    private static string? Th08Route(ReplayDocument replay)
    {
        var stages = replay.Stages.Select(stage => stage.StageId).ToHashSet();
        var hasA = stages.Contains(7); var hasB = stages.Contains(8); var hasOther = stages.Any(stage => stage is not 7 and not 8);
        if (Enumerable.Range(1, 9).All(stages.Contains)) return "9";
        if (hasA && hasB) return "6";
        if (hasA && hasOther) return "A";
        if (hasB && hasOther) return "B";
        return null;
    }

    private static string? Route(int route) => new[] { "A1", "A2", "B1", "B2", "C1", "C2", "Ex" }.ElementAtOrDefault(route);

    private static string? Character(string game, int value)
    {
        string[] names = game switch
        {
            "th06" or "th07" or "th10" or "th11" => ["Reimu", "Marisa", "Sakuya"],
            "th12" => ["Reimu", "Marisa", "Sanae"],
            "th08" => ["RM&YK", "MS&AL", "SK&RR", "YM&YY", "Reimu", "Yukari", "Marisa", "Alice", "Sakuya", "Remilia", "Youmu", "Yuyuko"],
            "th09" => ["Reimu", "Marisa", "Sakuya", "Youmu", "Reisen", "Cirno", "Lyrica", "Mystia", "Tewi", "Yuka", "Aya", "Medicine", "Komachi", "Sikieiki", "Merlin", "Lunasa"],
            "th125" => ["Aya", "Hatate"],
            "th13" => ["Reimu", "Marisa", "Sanae", "Youmu"],
            "th14" => ["Reimu", "Marisa", "Sakuya"],
            "th15" => ["Reimu", "Marisa", "Sanae", "Reisen"],
            "th16" => ["Reimu", "Cirno", "Aya", "Marisa"],
            "th17" => ["Reimu", "Marisa", "Youmu"],
            "th18" => ["Reimu", "Marisa", "Sakuya", "Sanae"],
            "th20" => ["Reimu", "Marisa"],
            _ => []
        };
        return value >= 0 && value < names.Length ? $"{names[value]}.png" : null;
    }
}

internal static class ShellRenderer
{
    private static readonly IReadOnlyDictionary<string, Color> Colors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
    {
        ["th06"] = Color.FromRgb(255,138,138), ["th07"] = Color.FromRgb(255,142,210), ["th08"] = Color.FromRgb(153,153,255),
        ["th09"] = Color.FromRgb(56,214,168), ["th095"] = Color.FromRgb(84,201,212), ["th10"] = Color.FromRgb(255,212,119),
        ["th11"] = Color.FromRgb(214,160,160), ["th12"] = Color.FromRgb(130,168,255), ["th125"] = Color.FromRgb(183,154,216),
        ["th128"] = Color.FromRgb(57,221,226), ["th13"] = Color.FromRgb(140,207,222), ["th14"] = Color.FromRgb(225,154,154),
        ["th143"] = Color.FromRgb(255,112,104), ["th15"] = Color.FromRgb(181,140,255), ["th16"] = Color.FromRgb(142,230,140),
        ["th165"] = Color.FromRgb(212,122,216), ["th17"] = Color.FromRgb(198,173,198), ["th18"] = Color.FromRgb(102,235,198),
        ["th20"] = Color.FromRgb(141,228,228)
    };
    public static BitmapSource Render(ShellMetadata metadata, int requested, bool details)
    {
        var size = Math.Clamp(requested, 16, 1024); const double logical = 512;
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.PushTransform(new ScaleTransform(size / logical, size / logical));
            var color = Colors.GetValueOrDefault(metadata.Game, Color.FromRgb(184,184,184));
            var border = new Pen(Brushes.Black, 4);
            drawing.DrawRectangle(Brushes.White, border, new Rect(30, 78, 452, 400));
            drawing.DrawRectangle(new SolidColorBrush(color), border, new Rect(30, 62, 452, 52));
            var version = metadata.Game.StartsWith("th", StringComparison.OrdinalIgnoreCase) ? metadata.Game[2..] : "";
            var badgeWidth = version.Length >= 3 ? 210d : 165d;
            var badge = new Rect(446 - badgeWidth, 16, badgeWidth, 108); drawing.DrawRectangle(Brushes.White, border, badge);
            CenteredText(drawing, version, 92, FontWeights.Bold, badge);
            if (details)
            {
                if (metadata.LowerLeftLabel is { Length: > 0 }) DrawLowerLeftLabel(drawing, metadata.LowerLeftLabel);
                else DrawAsset(drawing, metadata.DifficultyAsset, new Rect(40, 328, 145, 140));
                var characterRight = metadata.ShotTypeLabel is null ? 463d : 441.5d;
                var characterBottom = metadata.ShotTypeLabel is null ? 468d : 449d;
                var character = DrawAssetRight(drawing, metadata.CharacterAsset, characterRight, characterBottom - 171, 193.5, 171);
                if (character is Rect target && metadata.SpecialAsset is { Length: > 0 }) DrawSpecialAsset(drawing, metadata.SpecialAsset, target);
                if (character is Rect labelTarget && metadata.ShotTypeLabel is { Length: > 0 })
                    DrawOutlinedType(drawing, metadata.ShotTypeLabel, labelTarget, metadata.Game == "th128");
                var displayName = metadata.Name.Length > 4 ? metadata.Name[..4] + "\n" + metadata.Name[4..] : metadata.Name;
                Text(drawing, displayName, 94, FontWeights.Bold, new Rect(40, 112, 410, 250));
            }
            else CenteredText(drawing, "REP", 116, FontWeights.Bold, new Rect(40, 145, 432, 315));
            drawing.Pop();
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }

    public static string Icon(string game)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RepViewer", "icons");
        Directory.CreateDirectory(directory); var safe = NormalizeGameId(game); var path = Path.Combine(directory, $"{safe}.ico");
        if (File.Exists(path)) return path;
        int[] sizes = [16, 24, 32, 48, 64, 128, 256]; var images = sizes.Select(size => Png(Render(new ShellMetadata(safe, "", "", null, null, null, null), size, false))).ToArray();
        using var stream = File.Create(path); using var writer = new BinaryWriter(stream, Encoding.UTF8, false);
        writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length); var offset = 6 + sizes.Length * 16;
        for (var index = 0; index < sizes.Length; index++) { writer.Write((byte)(sizes[index] == 256 ? 0 : sizes[index])); writer.Write((byte)(sizes[index] == 256 ? 0 : sizes[index])); writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(images[index].Length); writer.Write(offset); offset += images[index].Length; }
        foreach (var image in images) writer.Write(image); return path;
    }

    private static string NormalizeGameId(string? game)
    {
        if (string.IsNullOrWhiteSpace(game)) return "default";
        return game.ToLowerInvariant().Replace(".", "", StringComparison.Ordinal).Replace("trial", "", StringComparison.Ordinal);
    }

    private static byte[] Png(BitmapSource bitmap) { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray(); }
    private static BitmapImage? LoadAsset(string? file)
    {
        if (string.IsNullOrWhiteSpace(file)) return null;
        var path = Path.Combine(Path.GetDirectoryName(typeof(ShellRenderer).Assembly.Location) ?? AppContext.BaseDirectory, "icons", "thumbnail", file);
        if (!File.Exists(path)) return null;
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.UriSource = new Uri(path); image.EndInit(); image.Freeze(); return image;
    }
    private static void DrawAsset(DrawingContext drawing, string? file, Rect bounds)
    {
        var image = LoadAsset(file); if (image is null) return; var ratio = Math.Min(bounds.Width / image.PixelWidth, bounds.Height / image.PixelHeight);
        drawing.DrawImage(image, new Rect(bounds.Left, bounds.Bottom - image.PixelHeight * ratio, image.PixelWidth * ratio, image.PixelHeight * ratio));
    }
    private static Rect? DrawAssetRight(DrawingContext drawing, string? file, double right, double top, double maxWidth, double maxHeight)
    {
        var image = LoadAsset(file); if (image is null) return null; var ratio = Math.Min(maxWidth / image.PixelWidth, maxHeight / image.PixelHeight);
        var rect = new Rect(right - image.PixelWidth * ratio, top + maxHeight - image.PixelHeight * ratio, image.PixelWidth * ratio, image.PixelHeight * ratio); drawing.DrawImage(image, rect); return rect;
    }
    private static void DrawSpecialAsset(DrawingContext drawing, string file, Rect character)
    {
        var image = LoadAsset(file); if (image is not null) drawing.DrawImage(image, OverlayBounds(character));
    }
    private static void DrawOutlinedType(DrawingContext drawing, string text, Rect character, bool preserveSingleGlyphHeight)
    {
        var overlay = OverlayBounds(character);
        var formatted = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Arial"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), character.Height, Brushes.Black, 1);
        var geometry = formatted.BuildGeometry(new Point()); var glyph = geometry.Bounds;
        var scale = (preserveSingleGlyphHeight ? overlay.Height / glyph.Height : Math.Min(overlay.Width / glyph.Width, overlay.Height / glyph.Height)) * 0.94;
        var left = Math.Max(34, Math.Min(overlay.Left + overlay.Width * 0.5, 478 - glyph.Width * scale));
        drawing.PushTransform(new TranslateTransform(left, overlay.Top)); drawing.PushTransform(new ScaleTransform(scale, scale)); drawing.PushTransform(new TranslateTransform(-glyph.Left, -glyph.Top));
        drawing.DrawGeometry(Brushes.Black, new Pen(Brushes.White, 6 / scale), geometry);
        drawing.Pop(); drawing.Pop(); drawing.Pop();
    }
    private static void DrawLowerLeftLabel(DrawingContext drawing, string text)
    {
        var bounds = new Rect(38, 342, 235, 118); const double maximum = 90;
        var formatted = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Arial"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), maximum, Brushes.Black, 1);
        if (formatted.WidthIncludingTrailingWhitespace > bounds.Width)
            formatted = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Arial"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), maximum * bounds.Width / formatted.WidthIncludingTrailingWhitespace, Brushes.Black, 1);
        drawing.DrawText(formatted, new Point(bounds.Left, bounds.Top + (bounds.Height - formatted.Height) / 2));
    }
    private static Rect OverlayBounds(Rect character)
    {
        var width = character.Width * 0.5; var height = character.Height * 0.5;
        var left = Math.Min(character.Right - width + character.Width * 0.1, 474 - width);
        var top = Math.Min(character.Bottom - height + character.Height * 0.1, 470 - height);
        return new Rect(Math.Max(34, left), Math.Max(82, top), width, height);
    }
    private static void CenteredText(DrawingContext drawing, string text, double size, FontWeight weight, Rect bounds)
    {
        var formatted = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Cambria"), FontStyles.Normal, weight, FontStretches.Normal), size, Brushes.Black, 1);
        drawing.DrawText(formatted, new Point(bounds.Left + (bounds.Width - formatted.WidthIncludingTrailingWhitespace) / 2, bounds.Top + (bounds.Height - formatted.Height) / 2));
    }
    private static void Text(DrawingContext drawing, string text, double size, FontWeight weight, Rect bounds)
    {
        var formatted = new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal), size, Brushes.Black, 1)
            { MaxTextWidth = bounds.Width, LineHeight = 104 };
        drawing.DrawText(formatted, bounds.TopLeft);
    }
}

[ComVisible(true), Guid(ClassId), ClassInterface(ClassInterfaceType.None)]
public sealed class ReplayThumbnailProvider : IInitializeWithFile, IInitializeWithStream, IThumbnailProvider
{
    public const string ClassId = "C43D4E2A-6154-4B6D-A23A-5FE2BB809D57";
    private string? _path; private byte[]? _bytes;
    public int Initialize(string filePath, uint mode) { _path = filePath; _bytes = null; return 0; }
    public int Initialize(IStream stream, uint mode) { try { _bytes = ReadAll(stream); _path = null; return 0; } catch { return HResult.Fail; } }
    public int GetThumbnail(uint size, out nint bitmap, out WtsAlphaType alphaType)
    {
        bitmap = 0; alphaType = WtsAlphaType.Argb;
        try { var replay = _bytes is not null ? ReplayApi.Read(_bytes) : _path is not null ? ReplayApi.ReadFile(_path) : null; bitmap = NativeBitmap.Create(ShellRenderer.Render(replay is null ? ShellMetadata.Empty : ShellMetadata.From(replay), checked((int)size), true)); return bitmap == 0 ? HResult.Fail : 0; }
        catch { bitmap = NativeBitmap.Create(ShellRenderer.Render(ShellMetadata.Empty, checked((int)size), false)); return bitmap == 0 ? HResult.Fail : 0; }
    }
    private static byte[] ReadAll(IStream stream)
    {
        stream.Stat(out var stat, 1); if (stat.cbSize is < 0 or > int.MaxValue) throw new InvalidDataException(); var result = new byte[(int)stat.cbSize]; var pointer = Marshal.AllocCoTaskMem(4);
        try { var offset = 0; while (offset < result.Length) { var chunk = new byte[Math.Min(65536, result.Length - offset)]; stream.Read(chunk, chunk.Length, pointer); var read = Marshal.ReadInt32(pointer); if (read <= 0) break; Buffer.BlockCopy(chunk, 0, result, offset, read); offset += read; } return result[..offset]; }
        finally { Marshal.FreeCoTaskMem(pointer); }
    }
}

[ComVisible(true), Guid(ClassId), ClassInterface(ClassInterfaceType.None)]
public sealed class ReplayIconHandler : IInitializeWithFile, IExtractIconW
{
    public const string ClassId = "23E70D1B-F04E-45D5-BAB7-C8A64E0B45F9"; private string? _path;
    public int Initialize(string filePath, uint mode) { _path = filePath; return 0; }
    public int GetIconLocation(uint flags, StringBuilder iconFile, uint maxLength, out int iconIndex, out uint resultFlags)
    {
        iconIndex = 0; resultFlags = 2;
        try { iconFile.Append(ShellRenderer.Icon(_path is null ? "default" : ReplayApi.ReadFile(_path).Identity.FormatId)); }
        catch { iconFile.Append(ShellRenderer.Icon("default")); }
        return 0;
    }
    public int Extract(string file, uint iconIndex, out nint largeIcon, out nint smallIcon, uint iconSize) { largeIcon = smallIcon = 0; return 1; }
}

[ComImport, Guid("B7D14566-0509-4CCE-A71F-0A554233BD9B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IInitializeWithFile { [PreserveSig] int Initialize([MarshalAs(UnmanagedType.LPWStr)] string filePath, uint mode); }
[ComImport, Guid("B824B49D-22AC-4161-AC8A-9916E8FA3F7F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IInitializeWithStream { [PreserveSig] int Initialize([MarshalAs(UnmanagedType.Interface)] IStream stream, uint mode); }
[ComImport, Guid("E357FCCD-A995-4576-B01F-234630154E96"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IThumbnailProvider { [PreserveSig] int GetThumbnail(uint size, out nint bitmap, out WtsAlphaType alphaType); }
[ComImport, Guid("000214FA-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IExtractIconW { [PreserveSig] int GetIconLocation(uint flags, [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconFile, uint maxLength, out int iconIndex, out uint resultFlags); [PreserveSig] int Extract([MarshalAs(UnmanagedType.LPWStr)] string file, uint iconIndex, out nint largeIcon, out nint smallIcon, uint iconSize); }
public enum WtsAlphaType { Unknown, Rgb, Argb }
internal static class HResult { public const int Fail = unchecked((int)0x80004005); }

internal static class NativeBitmap
{
    public static nint Create(BitmapSource source)
    {
        var stride = source.PixelWidth * 4; var pixels = new byte[stride * source.PixelHeight]; source.CopyPixels(pixels, stride, 0);
        var info = new BitmapInfo { Header = new BitmapInfoHeader { Size = 40, Width = source.PixelWidth, Height = -source.PixelHeight, Planes = 1, BitCount = 32 } };
        var bitmap = CreateDibSection(0, ref info, 0, out var bits, 0, 0); if (bitmap != 0) Marshal.Copy(pixels, 0, bits, pixels.Length); return bitmap;
    }
    [DllImport("gdi32.dll", EntryPoint = "CreateDIBSection")] private static extern nint CreateDibSection(nint dc, ref BitmapInfo info, uint usage, out nint bits, nint section, uint offset);
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo { public BitmapInfoHeader Header; public uint Color; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfoHeader { public uint Size; public int Width; public int Height; public ushort Planes; public ushort BitCount; public uint Compression; public uint ImageSize; public int XPelsPerMeter; public int YPelsPerMeter; public uint ColorsUsed; public uint ColorsImportant; }
}
