using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using RepViewer.Core;
using RepViewer.Plugins;
using RepViewer.Presentation;

namespace RepViewer.App;

public sealed class KeyRateChartStack : Grid
{
    public KeyRateChartStack(ReplayView view, PresentationCatalog presentation, Action editPunishment, ChartNumberFormatOptions? numberFormat = null)
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition());
        var edit = new Button { Content = presentation.Text("command.punishment"), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(8, 4, 8, 4) };
        edit.Click += (_, _) => editPunishment();
        Children.Add(edit);
        var stack = new StackPanel();
        var series = view.Series ?? [];
        for (var index = 0; index < series.Count; index++)
        {
            var panel = new Grid();
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); panel.RowDefinitions.Add(new RowDefinition());
            panel.Children.Add(new TextBlock { Text = series[index].Label, FontWeight = FontWeights.SemiBold, FontSize = 15, Margin = new Thickness(8, 3, 0, 0) });
            var chart = new LineChartPanel(view with { Series = [series[index]] }, presentation, numberFormat) { Height = 275 };
            Grid.SetRow(chart, 1); panel.Children.Add(chart); stack.Children.Add(panel);
        }
        var scroll = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); Children.Add(scroll);
    }
}

public sealed class LineChartPanel : Grid
{
    public InteractiveLineChart Chart { get; }

    public LineChartPanel(ReplayView view, PresentationCatalog presentation, ChartNumberFormatOptions? numberFormat = null)
    {
        RowDefinitions.Add(new RowDefinition());
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Chart = new InteractiveLineChart { NumberFormat = numberFormat ?? new ChartNumberFormatOptions(), Data = view, MinHeight = 180 };
        var saveCsv = new MenuItem { Header = presentation.Text("command.saveChartCsv") };
        var chartMenu = new ContextMenu { Items = { saveCsv } };
        chartMenu.Opened += (_, _) => saveCsv.IsEnabled = Chart.ExportData() is { Points.Count: > 0 };
        saveCsv.Click += (_, _) => SaveCsv();
        Chart.ContextMenu = chartMenu;
        Children.Add(Chart);
        var statistics = new TextBlock { Margin = new Thickness(64, 3, 12, 5), Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Text = presentation.Text("chart.selection.none") };
        Grid.SetRow(statistics, 1); Children.Add(statistics);
        Chart.SelectionStatisticsChanged += (_, value) => statistics.Text = value is null
            ? presentation.Text("chart.selection.none")
            : $"{presentation.Text("chart.selection.range")} {FormatX(value.Start)}–{FormatX(value.End)}  ·  " +
              $"{presentation.Text("chart.selection.count")} {value.Count}  ·  {presentation.Text("chart.selection.average")} {Chart.FormatNumber(value.Average)}  ·  " +
              $"{presentation.Text("chart.selection.variance")} {Chart.FormatNumber(value.Variance)}  ·  {presentation.Text("chart.selection.minimum")} {Chart.FormatNumber(value.Minimum)}  ·  " +
              $"{presentation.Text("chart.selection.maximum")} {Chart.FormatNumber(value.Maximum)}";

        string FormatX(double value) => view.Metadata?.GetValueOrDefault("xIsFrame") is true
            ? ReplayFrameTime.Format((int)Math.Round(value))
            : value.ToString("0.##", CultureInfo.CurrentCulture);

        void SaveCsv()
        {
            if (Chart.ExportData() is not { Points.Count: > 0 } export) return;
            var invalid = Path.GetInvalidFileNameChars();
            var name = new string(export.SeriesLabel.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
            var dialog = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = $"{name}.csv" };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
            var lines = new string[export.Points.Count + 1];
            lines[0] = "x,y";
            for (var index = 0; index < export.Points.Count; index++)
                lines[index + 1] = $"{export.Points[index].X.ToString("G17", CultureInfo.InvariantCulture)},{export.Points[index].Y.ToString("G17", CultureInfo.InvariantCulture)}";
            File.WriteAllLines(dialog.FileName, lines, new UTF8Encoding(true));
        }
    }
}

public sealed class TransitionStatisticsPanel : Grid
{
    private readonly TransitionHistogram _histogram;
    public TransitionStatisticsPanel(ReplayView view, PresentationCatalog presentation)
    {
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(3, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) });
        var grid = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Extended, SelectionUnit = DataGridSelectionUnit.Cell, Margin = new Thickness(6) };
        DataGridFeatures.EnableCopy(grid, presentation);
        foreach (var column in view.Columns ?? [])
        {
            var index = grid.Columns.Count;
            grid.Columns.Add(new DataGridTextColumn { Header = column.Label, Binding = new System.Windows.Data.Binding($"[{index}]"), CanUserSort = false, Width = index == 0 ? 72 : 48 });
        }
        grid.ItemsSource = (view.Rows ?? []).Select(row => row.Cells.ToArray()).ToArray(); Children.Add(grid);
        _histogram = new TransitionHistogram { StageBoundaries = view.Metadata?.GetValueOrDefault("stageBoundaries") as IReadOnlyList<int> ?? [], Margin = new Thickness(6) };
        Grid.SetRow(_histogram, 1); Children.Add(_histogram);
        grid.SelectedCellsChanged += (_, _) =>
        {
            if (view.Metadata?.GetValueOrDefault("frames") is not List<int>[][] frames) return;
            var selected = new HashSet<int>();
            foreach (var cell in grid.SelectedCells)
            {
                var from = grid.Items.IndexOf(cell.Item); var to = cell.Column.DisplayIndex - 1;
                if (from < 0 || to < 0 || from >= 16 || to >= 16) continue;
                foreach (var frame in frames[from][to]) selected.Add(frame);
            }
            _histogram.Frames = selected.Order().ToArray(); _histogram.InvalidateVisual();
        };
    }
}

public sealed class TransitionHistogram : FrameworkElement
{
    public IReadOnlyList<int> Frames { get; set; } = [];
    public IReadOnlyList<int> StageBoundaries { get; set; } = [];
    public int BinCount { get; set; } = 30;
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.White, null, new Rect(RenderSize));
        const double left = 56, right = 14, top = 18, bottom = 38;
        var width = Math.Max(1, ActualWidth - left - right); var height = Math.Max(1, ActualHeight - top - bottom);
        var maxFrame = Math.Max(1, Math.Max(Frames.Count == 0 ? 1 : Frames.Max(), StageBoundaries.Count == 0 ? 1 : StageBoundaries.Max()));
        var bins = new int[Math.Max(1, BinCount)];
        foreach (var frame in Frames) bins[Math.Min(bins.Length - 1, (int)((long)frame * bins.Length / (maxFrame + 1)))]++;
        var maxCount = Math.Max(1, bins.Max()); var grid = new Pen(new SolidColorBrush(Color.FromRgb(215, 218, 224)), 1);
        for (var line = 0; line <= 4; line++) { var y = top + height * line / 4; dc.DrawLine(grid, new Point(left, y), new Point(left + width, y)); Text(dc, (maxCount * (4 - line) / 4).ToString(), 4, y - 8); }
        var barWidth = width / bins.Length;
        for (var index = 0; index < bins.Length; index++) { var h = height * bins[index] / maxCount; dc.DrawRectangle(Brushes.SteelBlue, null, new Rect(left + index * barWidth + 1, top + height - h, Math.Max(1, barWidth - 2), h)); }
        foreach (var boundary in StageBoundaries) { var x = left + width * boundary / maxFrame; dc.DrawLine(new Pen(Brushes.IndianRed, 1.5), new Point(x, top), new Point(x, top + height)); }
        for (var frame = 0; frame <= maxFrame; frame += Math.Max(60, maxFrame / 8 / 60 * 60)) { var x = left + width * frame / maxFrame; dc.DrawLine(grid, new Point(x, top), new Point(x, top + height)); Text(dc, ReplayFrameTime.Format(frame), x + 2, top + height + 5); }
        Text(dc, "次数", 5, 1); Text(dc, "帧时间", left + width - 48, top + height + 21);
    }
    private static void Text(DrawingContext dc, string text, double x, double y) => dc.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, Brushes.DimGray, 1), new Point(x, y));
}
