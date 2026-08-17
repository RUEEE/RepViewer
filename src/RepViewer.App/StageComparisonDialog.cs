using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using RepViewer.Core;
using RepViewer.Plugins;
using RepViewer.Presentation;

namespace RepViewer.App;

internal sealed class StageComparisonDialog : Window
{
    private readonly ReplayDocument _baseReplay;
    private readonly string _basePath;
    private readonly string _fieldName;
    private readonly string _fieldLabel;
    private readonly PresentationCatalog _presentation;
    private readonly List<ComparisonLine> _lines = [];
    private readonly StackPanel _lineRows = new();
    private readonly InteractiveLineChart _chart;
    private ComparisonLine? _selected;
    private int _nextLineId = 1;

    public StageComparisonDialog(ReplayDocument replay, string replayPath, string fieldName, string fieldLabel, PresentationCatalog presentation,
        ChartNumberFormatOptions? numberFormat = null)
    {
        _baseReplay = replay;
        _basePath = replayPath;
        _fieldName = fieldName;
        _fieldLabel = fieldLabel;
        _presentation = presentation;
        Title = $"{presentation.Text("dialog.stageChart.title")} — {fieldLabel}";
        Width = 1120;
        Height = 700;
        MinWidth = 780;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        if (!TryCreateSeries(replay, fieldName, "rep-0", Path.GetFileName(replayPath), out var initial))
            throw new InvalidOperationException(presentation.Text("dialog.stageChart.fieldUnavailable"));
        var first = new ComparisonLine(initial, replayPath, Fingerprint(replay), HsvColor(0));
        _lines.Add(first);
        _selected = first;

        var view = CreateView();
        var chartPanel = new LineChartPanel(view, presentation, numberFormat) { Margin = new Thickness(8) };
        _chart = chartPanel.Chart;

        var compare = new Button
        {
            Content = presentation.Text("command.compare"),
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 92,
            Margin = new Thickness(12, 4, 8, 12)
        };
        compare.Click += CompareClick;

        var left = new Grid();
        left.RowDefinitions.Add(new RowDefinition());
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.Children.Add(chartPanel);
        Grid.SetRow(compare, 1);
        left.Children.Add(compare);

        var right = new DockPanel { Margin = new Thickness(0, 10, 10, 10) };
        var header = RowGrid();
        Add(header, new TextBlock { Text = presentation.Text("column.enabled"), FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center }, 0);
        Add(header, new TextBlock { Text = presentation.Text("column.replay"), FontWeight = FontWeights.SemiBold }, 1);
        Add(header, new TextBlock { Text = presentation.Text("column.color"), FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center }, 2);
        DockPanel.SetDock(header, Dock.Top);
        right.Children.Add(header);
        var deleteHint = new TextBlock { Text = presentation.Text("dialog.stageChart.deleteHint"), Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 8, 4, 2) };
        DockPanel.SetDock(deleteHint, Dock.Bottom);
        right.Children.Add(deleteHint);
        right.Children.Add(new ScrollViewer { Content = _lineRows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        var root = new Grid { Background = new SolidColorBrush(Color.FromRgb(244, 245, 247)) };
        root.ColumnDefinitions.Add(new ColumnDefinition());
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(310) });
        root.Children.Add(left);
        Grid.SetColumn(right, 1);
        root.Children.Add(right);
        Content = root;
        PreviewKeyDown += DeleteSelectedLine;
        RebuildLineRows();
        RefreshChart();
    }

    public static bool CanPlot(ReplayDocument replay, string fieldName, PresentationCatalog presentation)
    {
        if (replay.Stages.Count <= 1 || presentation.Field($"stage.{fieldName}").Values is { Count: > 0 }) return false;
        return TryCreateSeries(replay, fieldName, "probe", fieldName, out _);
    }

    private ReplayView CreateView() => new("stage-field-chart", _fieldLabel, ReplayViewKind.LineChart,
        Series: _lines.Select(line => line.Series).ToArray(),
        Metadata: new Dictionary<string, object?>
        {
            ["xUnit"] = _presentation.Text("chart.stageSequence"),
            ["xInteger"] = true,
            ["yUnit"] = _fieldLabel,
            ["interactionHint"] = _presentation.Text("chart.interactionHint"),
            ["averageLabel"] = _presentation.Text("chart.average"),
            ["showAllAnchors"] = true
        });

    private void CompareClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Touhou replay (*.rpy)|*.rpy|All files (*.*)|*.*",
            InitialDirectory = Path.GetDirectoryName(_basePath)
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var replay = ReplayApi.ReadFile(dialog.FileName);
            var fingerprint = Fingerprint(replay);
            if (_lines.Any(line => line.Fingerprint.Equals(fingerprint, StringComparison.Ordinal)))
            {
                MessageBox.Show(this, _presentation.Text("dialog.stageChart.duplicate"), "RepViewer", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (!string.Equals(replay.Identity.FormatId, _baseReplay.Identity.FormatId, StringComparison.OrdinalIgnoreCase) ||
                replay.Stages.Count != _baseReplay.Stages.Count)
            {
                ShowOpenFailure(_presentation.Text("dialog.stageChart.incompatible"));
                return;
            }
            var id = $"rep-{_nextLineId++}";
            if (!TryCreateSeries(replay, _fieldName, id, Path.GetFileName(dialog.FileName), out var series))
            {
                ShowOpenFailure(_presentation.Text("dialog.stageChart.fieldUnavailable"));
                return;
            }
            var line = new ComparisonLine(series, dialog.FileName, fingerprint, HsvColor(_lines.Count));
            _lines.Add(line);
            _selected = line;
            RebuildLineRows();
            RefreshChart();
        }
        catch (Exception exception)
        {
            ShowOpenFailure(exception.Message);
        }
    }

    private void ShowOpenFailure(string detail) => MessageBox.Show(this,
        $"{_presentation.Text("dialog.stageChart.openFailed")}\n{detail}", "RepViewer", MessageBoxButton.OK, MessageBoxImage.Error);

    private void RebuildLineRows()
    {
        _lineRows.Children.Clear();
        foreach (var line in _lines)
        {
            var row = RowGrid();
            var check = new CheckBox { IsChecked = line.Enabled, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            check.Checked += (_, _) => { line.Enabled = true; Select(line); RefreshChart(); };
            check.Unchecked += (_, _) =>
            {
                line.Enabled = false;
                if (ReferenceEquals(_selected, line)) _selected = _lines.FirstOrDefault(item => item.Enabled);
                RebuildLineRows();
                RefreshChart();
            };
            Add(row, check, 0);

            var name = new TextBlock { Text = line.Series.Label, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, ToolTip = line.Path };
            name.MouseLeftButtonDown += (_, _) => Select(line);
            Add(row, name, 1);

            var color = new Button
            {
                Content = $"#{line.Color.R:X2}{line.Color.G:X2}{line.Color.B:X2}",
                Background = new SolidColorBrush(line.Color),
                Foreground = ReadableText(line.Color),
                Padding = new Thickness(3, 1, 3, 1),
                Margin = new Thickness(4, 2, 4, 2)
            };
            color.Click += (_, _) =>
            {
                var picker = new HsvColorDialog(line.Color, _presentation) { Owner = this };
                if (picker.ShowDialog() != true) return;
                line.Color = picker.Color;
                Select(line);
                RebuildLineRows();
                RefreshChart();
            };
            Add(row, color, 2);

            var border = new Border
            {
                Child = row,
                BorderBrush = new SolidColorBrush(Color.FromRgb(216, 220, 227)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Background = ReferenceEquals(line, _selected) ? new SolidColorBrush(Color.FromRgb(224, 235, 252)) : Brushes.White,
                Padding = new Thickness(0, 3, 0, 3)
            };
            _lineRows.Children.Add(border);
        }
    }

    private void Select(ComparisonLine line)
    {
        var enabledChanged = !line.Enabled;
        line.Enabled = true;
        _selected = line;
        RebuildLineRows();
        if (enabledChanged) RefreshChart();
        else _chart.SelectedSeriesId = line.Series.Id;
    }

    private void DeleteSelectedLine(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Delete or Key.Back) || _selected is null) return;
        var index = _lines.IndexOf(_selected);
        if (index < 0) return;
        _lines.RemoveAt(index);
        _selected = _lines.Count == 0 ? null : _lines[Math.Min(index, _lines.Count - 1)];
        RebuildLineRows();
        RefreshChart();
        e.Handled = true;
    }

    private void RefreshChart()
    {
        _chart.Data = CreateView();
        _chart.EnabledSeriesIds = _lines.Where(line => line.Enabled).Select(line => line.Series.Id).ToHashSet(StringComparer.Ordinal);
        _chart.SeriesColors = _lines.ToDictionary(line => line.Series.Id, line => line.Color, StringComparer.Ordinal);
        _chart.SelectedSeriesId = _selected is { Enabled: true } ? _selected.Series.Id : null;
    }

    private static Grid RowGrid()
    {
        var grid = new Grid { MinHeight = 30 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        return grid;
    }

    private static void Add(Grid grid, UIElement child, int column)
    {
        Grid.SetColumn(child, column);
        grid.Children.Add(child);
    }

    private static bool TryCreateSeries(ReplayDocument replay, string fieldName, string id, string label, out ReplayViewSeries series)
    {
        var points = new List<ReplayViewPoint>(replay.Stages.Count);
        for (var index = 0; index < replay.Stages.Count; index++)
        {
            var node = replay.StageRoot.Children[index].Children.FirstOrDefault(child =>
                child.Kind == ReplayNodeType.Value && child.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
            if (node is null || !TryNumber(node.SemanticValue, out var value))
            {
                series = new ReplayViewSeries(id, label, []);
                return false;
            }
            points.Add(new ReplayViewPoint(index + 1, value));
        }
        series = new ReplayViewSeries(id, label, points);
        return points.Count > 1;
    }

    private static bool TryNumber(object? value, out double number)
    {
        switch (value)
        {
            case byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal:
                number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return !double.IsNaN(number) && !double.IsInfinity(number);
            default:
                number = 0;
                return false;
        }
    }

    internal static Color HsvColor(double hue, double saturation, double value)
    {
        hue = hue - Math.Floor(hue);
        saturation = Math.Clamp(saturation, 0, 1);
        value = Math.Clamp(value, 0, 1);
        var sector = (hue - Math.Floor(hue)) * 6;
        var part = sector - Math.Floor(sector);
        var p = value * (1 - saturation);
        var q = value * (1 - saturation * part);
        var t = value * (1 - saturation * (1 - part));
        var (r, g, b) = ((int)Math.Floor(sector)) switch
        {
            0 => (value, t, p),
            1 => (q, value, p),
            2 => (p, value, t),
            3 => (p, q, value),
            4 => (t, p, value),
            _ => (value, p, q)
        };
        return Color.FromRgb((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
    }

    private static Color HsvColor(int index) => HsvColor(index / 6d, 0.75, 0.75);
    private static string Fingerprint(ReplayDocument replay) => Convert.ToHexString(SHA256.HashData(replay.OriginalBytes.Span));

    private static Brush ReadableText(Color color) => color.R * 0.299 + color.G * 0.587 + color.B * 0.114 > 150 ? Brushes.Black : Brushes.White;

    private sealed class ComparisonLine(ReplayViewSeries series, string path, string fingerprint, Color color)
    {
        public ReplayViewSeries Series { get; } = series;
        public string Path { get; } = path;
        public string Fingerprint { get; } = fingerprint;
        public bool Enabled { get; set; } = true;
        public Color Color { get; set; } = color;
    }
}

internal sealed class HsvColorDialog : Window
{
    private readonly TextBox _hex;
    private readonly Slider _hue;
    private readonly Slider _saturation;
    private readonly Slider _brightness;
    private readonly Border _saturationBar;
    private readonly Border _brightnessBar;
    private readonly Border _preview;
    private readonly TextBlock _hueLabel;
    private readonly TextBlock _saturationLabel;
    private readonly TextBlock _brightnessLabel;
    private bool _updating;
    public Color Color { get; private set; }

    public HsvColorDialog(Color initial, PresentationCatalog presentation)
    {
        Color = initial;
        Title = presentation.Text("dialog.color.title");
        Width = 420;
        Height = 440;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var (hue, saturation, brightness) = RgbToHsv(initial);
        _hue = new Slider { Minimum = 0, Maximum = 360, Value = hue, TickFrequency = 1 };
        _saturation = new Slider { Minimum = 0, Maximum = 100, Value = saturation * 100, TickFrequency = 1 };
        _brightness = new Slider { Minimum = 0, Maximum = 100, Value = brightness * 100, TickFrequency = 1 };
        _hueLabel = new TextBlock();
        _saturationLabel = new TextBlock();
        _brightnessLabel = new TextBlock();
        _saturationBar = Bar(Brushes.White);
        _brightnessBar = Bar(Brushes.Black);
        _preview = new Border { Height = 38, CornerRadius = new CornerRadius(3), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Margin = new Thickness(0, 5, 0, 12) };
        _hex = new TextBox { Text = Hex(initial), Margin = new Thickness(0, 4, 0, 12) };

        var content = new StackPanel { Margin = new Thickness(16) };
        AddChannel(content, _hueLabel, Bar(HueBrush()), _hue);
        AddChannel(content, _saturationLabel, _saturationBar, _saturation);
        AddChannel(content, _brightnessLabel, _brightnessBar, _brightness);
        content.Children.Add(new TextBlock { Text = presentation.Text("dialog.color.preview") });
        content.Children.Add(_preview);
        content.Children.Add(new TextBlock { Text = presentation.Text("dialog.color.hex") });
        content.Children.Add(_hex);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = presentation.Text("command.ok"), IsDefault = true };
        ok.Click += (_, _) =>
        {
            if (!TryHex(_hex.Text, out var color))
            {
                MessageBox.Show(this, presentation.Text("dialog.color.invalid"), "RepViewer", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Color = color;
            DialogResult = true;
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(new Button { Content = presentation.Text("command.cancel"), IsCancel = true });
        content.Children.Add(buttons);
        Content = content;

        _hue.ValueChanged += (_, _) => UpdateFromHsv(presentation);
        _saturation.ValueChanged += (_, _) => UpdateFromHsv(presentation);
        _brightness.ValueChanged += (_, _) => UpdateFromHsv(presentation);
        _hex.TextChanged += (_, _) =>
        {
            if (_updating || !TryHex(_hex.Text, out var color)) return;
            ApplyRgb(color, presentation);
        };
        UpdateVisuals(presentation, updateHex: true);
    }

    private void UpdateFromHsv(PresentationCatalog presentation)
    {
        if (_updating) return;
        Color = StageComparisonDialog.HsvColor(_hue.Value / 360d, _saturation.Value / 100d, _brightness.Value / 100d);
        UpdateVisuals(presentation, updateHex: true);
    }

    private void ApplyRgb(Color color, PresentationCatalog presentation)
    {
        _updating = true;
        Color = color;
        var (h, s, v) = RgbToHsv(color);
        _hue.Value = h;
        _saturation.Value = s * 100;
        _brightness.Value = v * 100;
        _updating = false;
        UpdateVisuals(presentation, updateHex: false);
    }

    private void UpdateVisuals(PresentationCatalog presentation, bool updateHex)
    {
        _updating = true;
        _hueLabel.Text = $"{presentation.Text("dialog.color.hue")}: {_hue.Value:0}°";
        _saturationLabel.Text = $"{presentation.Text("dialog.color.saturation")}: {_saturation.Value:0}%";
        _brightnessLabel.Text = $"{presentation.Text("dialog.color.value")}: {_brightness.Value:0}%";
        var currentValue = _brightness.Value / 100d;
        _saturationBar.Background = Gradient(StageComparisonDialog.HsvColor(_hue.Value / 360d, 0, currentValue), StageComparisonDialog.HsvColor(_hue.Value / 360d, 1, currentValue));
        _brightnessBar.Background = Gradient(Colors.Black, StageComparisonDialog.HsvColor(_hue.Value / 360d, _saturation.Value / 100d, 1));
        _preview.Background = new SolidColorBrush(Color);
        if (updateHex) _hex.Text = Hex(Color);
        _updating = false;
    }

    private static void AddChannel(Panel panel, TextBlock label, Border bar, Slider slider)
    {
        label.Margin = new Thickness(0, 0, 0, 2);
        panel.Children.Add(label);
        panel.Children.Add(bar);
        slider.Margin = new Thickness(0, 0, 0, 10);
        panel.Children.Add(slider);
    }

    private static Border Bar(Brush background) => new() { Height = 12, Background = background, CornerRadius = new CornerRadius(2), Margin = new Thickness(3, 0, 3, 1) };
    private static LinearGradientBrush Gradient(Color start, Color end) => new(start, end, new Point(0, 0.5), new Point(1, 0.5));
    private static LinearGradientBrush HueBrush()
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        foreach (var (offset, color) in new[] { (0d, Colors.Red), (1d / 6, Colors.Yellow), (2d / 6, Colors.Lime), (3d / 6, Colors.Cyan), (4d / 6, Colors.Blue), (5d / 6, Colors.Magenta), (1d, Colors.Red) })
            brush.GradientStops.Add(new GradientStop(color, offset));
        return brush;
    }

    private static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    private static bool TryHex(string text, out Color color)
    {
        text = text.Trim().TrimStart('#');
        if (text.Length == 6 && uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            color = System.Windows.Media.Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
            return true;
        }
        color = default;
        return false;
    }

    private static (double Hue, double Saturation, double Value) RgbToHsv(Color color)
    {
        var r = color.R / 255d;
        var g = color.G / 255d;
        var b = color.B / 255d;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        var hue = delta == 0 ? 0 : max == r ? 60 * (((g - b) / delta) % 6) : max == g ? 60 * ((b - r) / delta + 2) : 60 * ((r - g) / delta + 4);
        if (hue < 0) hue += 360;
        return (hue, max == 0 ? 0 : delta / max, max);
    }
}
