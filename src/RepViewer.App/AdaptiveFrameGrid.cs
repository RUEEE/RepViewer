using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using RepViewer.Plugins;
using RepViewer.Presentation;

namespace RepViewer.App;

public sealed class AdaptiveFrameGrid : Grid
{
    private static readonly int[] Divisors = [1, 2, 3, 4, 5, 6, 10, 12, 15, 20, 30, 60];
    private readonly DataGrid _grid;
    private readonly IReadOnlyList<ReplayViewRow> _frames;
    private int _columnCount;

    public AdaptiveFrameGrid(ReplayView view, PresentationCatalog presentation)
    {
        _frames = view.Rows ?? [];
        _grid = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, EnableRowVirtualization = true, EnableColumnVirtualization = true };
        DataGridFeatures.EnableCopy(_grid, presentation);
        Children.Add(_grid);
        Loaded += (_, _) => Rebuild(ActualWidth);
        SizeChanged += (_, e) => Rebuild(e.NewSize.Width);
    }

    private void Rebuild(double width)
    {
        var fit = Math.Max(1, (int)((width - 124) / 76));
        var columns = Divisors.Last(value => value <= Math.Min(60, fit));
        if (columns == _columnCount) return;
        _columnCount = columns;
        _grid.Columns.Clear();
        _grid.Columns.Add(new DataGridTextColumn { Header = "起始时间", Binding = new Binding(nameof(FrameRow.Start)), Width = 112, CanUserSort = false });
        for (var index = 0; index < columns; index++)
            _grid.Columns.Add(new DataGridTextColumn { Header = index.ToString(), Binding = new Binding($"Cells[{index}]"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), CanUserSort = false });
        var rows = new List<FrameRow>((_frames.Count + columns - 1) / columns);
        for (var start = 0; start < _frames.Count; start += columns)
        {
            var cells = new string[columns];
            for (var offset = 0; offset < columns && start + offset < _frames.Count; offset++)
            {
                var values = _frames[start + offset].Cells;
                var direction = Convert.ToString(values.ElementAtOrDefault(2)) ?? "";
                var action = Convert.ToString(values.ElementAtOrDefault(3)) ?? "";
                cells[offset] = action is "" or "—" ? direction : $"{direction}{action}";
            }
            rows.Add(new FrameRow(Convert.ToString(_frames[start].Cells.ElementAtOrDefault(1)) ?? start.ToString(), cells));
        }
        _grid.ItemsSource = rows;
    }

    private sealed record FrameRow(string Start, string[] Cells);
}
