using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using RepViewer.Plugins;
using RepViewer.Presentation;

namespace RepViewer.App;

public sealed class AdaptiveFpsGrid : Grid
{
    private static readonly int[] Divisors = [1, 2, 3, 4, 5, 6, 10, 12, 15, 20, 30, 60];
    private readonly DataGrid _grid;
    private readonly IReadOnlyList<ReplayViewRow> _samples;
    private readonly PresentationCatalog _presentation;
    private int _columnCount;

    public AdaptiveFpsGrid(ReplayView view, PresentationCatalog presentation)
    {
        _samples = view.Rows ?? []; _presentation = presentation;
        _grid = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, EnableRowVirtualization = true, EnableColumnVirtualization = true };
        DataGridFeatures.EnableCopy(_grid, presentation); Children.Add(_grid);
        Loaded += (_, _) => Rebuild(ActualWidth); SizeChanged += (_, e) => Rebuild(e.NewSize.Width);
    }

    private void Rebuild(double width)
    {
        var fit = Math.Max(1, (int)((width - 124) / 64));
        var columns = Divisors.Last(value => value <= Math.Min(60, fit));
        if (columns == _columnCount) return;
        _columnCount = columns; _grid.Columns.Clear();
        _grid.Columns.Add(new DataGridTextColumn { Header = _presentation.Text("column.timeSeconds"), Binding = new Binding(nameof(FpsRow.Time)), Width = 112, CanUserSort = false });
        for (var index = 0; index < columns; index++)
            _grid.Columns.Add(new DataGridTextColumn { Header = index.ToString(), Binding = new Binding($"Values[{index}]"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), CanUserSort = false });
        var rows = new List<FpsRow>((_samples.Count + columns - 1) / columns);
        for (var start = 0; start < _samples.Count; start += columns)
        {
            var values = new string[columns];
            for (var offset = 0; offset < columns && start + offset < _samples.Count; offset++)
                values[offset] = Convert.ToString(_samples[start + offset].Cells.ElementAtOrDefault(3)) ?? "";
            rows.Add(new FpsRow(Convert.ToString(_samples[start].Cells.ElementAtOrDefault(2)) ?? "", values));
        }
        _grid.ItemsSource = rows;
    }

    private sealed record FpsRow(string Time, string[] Values);
}
