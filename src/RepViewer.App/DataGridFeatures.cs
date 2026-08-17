using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RepViewer.Presentation;

namespace RepViewer.App;

internal static class DataGridFeatures
{
    public static void EnableCopy(DataGrid grid, PresentationCatalog presentation)
    {
        grid.ClipboardCopyMode = DataGridClipboardCopyMode.None;
        var copyCell = new MenuItem { Header = presentation.Text("command.copyCell") };
        var copyRow = new MenuItem { Header = presentation.Text("command.copyRow") };
        copyCell.Click += (_, _) => CopyCell(grid);
        copyRow.Click += (_, _) => CopyRow(grid);
        grid.ContextMenu = new ContextMenu { Items = { copyCell, copyRow } };
        grid.PreviewMouseRightButtonDown += (_, e) =>
        {
            var cell = Parent<DataGridCell>(e.OriginalSource as DependencyObject);
            if (cell is null)
            {
                grid.UnselectAll();
                grid.UnselectAllCells();
                grid.CurrentCell = default;
                return;
            }
            cell.Focus();
            grid.CurrentCell = new DataGridCellInfo(cell);
            if (grid.SelectionUnit == DataGridSelectionUnit.FullRow) grid.SelectedItem = cell.DataContext;
            else cell.IsSelected = true;
        };
        grid.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.C || (Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            CopyCell(grid);
            e.Handled = true;
        };
    }

    private static void CopyCell(DataGrid grid)
    {
        var current = grid.CurrentCell;
        if (!current.IsValid || current.Item is null || current.Column is null) return;
        SetClipboard(CellText(current.Item, current.Column));
    }

    private static void CopyRow(DataGrid grid)
    {
        var item = grid.CurrentItem ?? grid.SelectedItem;
        if (item is null) return;
        SetClipboard(string.Join('\t', grid.Columns.OrderBy(column => column.DisplayIndex).Select(column => CellText(item, column))));
    }

    private static string CellText(object item, DataGridColumn column)
    {
        if (column is DataGridBoundColumn { Binding: System.Windows.Data.Binding binding })
        {
            var path = binding.Path?.Path ?? "";
            if (path.StartsWith('[') && path.EndsWith(']') && int.TryParse(path[1..^1], out var index) && item is IList list && index < list.Count)
                return Convert.ToString(list[index]) ?? "";
            var property = item.GetType().GetProperty(path, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (property is not null) return Convert.ToString(property.GetValue(item)) ?? "";
        }
        return Convert.ToString(column.GetCellContent(item) is TextBlock text ? text.Text : item) ?? "";
    }

    private static void SetClipboard(string text)
    {
        try { Clipboard.SetText(text); }
        catch (ExternalException) { }
    }

    private static T? Parent<T>(DependencyObject? value) where T : DependencyObject
    {
        while (value is not null && value is not T) value = VisualTreeHelper.GetParent(value);
        return value as T;
    }
}
