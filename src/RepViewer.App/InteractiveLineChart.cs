using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using RepViewer.Plugins;

namespace RepViewer.App;

public sealed record ChartSelectionStatistics(double Start, double End, int Count, double Average, double Variance, double Minimum, double Maximum);
public sealed record ChartNumberFormatOptions(bool UseScientificNotation = false, bool UseThousandsSeparator = true);

/// <summary>Reusable line chart with axes, adaptive grid, stage boundaries, wheel zoom and drag pan.</summary>
public sealed class InteractiveLineChart : FrameworkElement
{
    private ReplayView? _data;
    private double _dataMinX;
    private double _dataMaxX = 1;
    private double _dataMinY;
    private double _dataMaxY = 1;
    private double _viewMinX;
    private double _viewMaxX = 1;
    private Point? _dragStart;
    private double _dragViewStart;
    private double _dragViewEnd;
    private double _dragViewMinY;
    private double _dragViewMaxY;
    private double? _viewMinY;
    private double? _viewMaxY;
    private double _renderMinY;
    private double _renderMaxY = 1;
    private double? _selectionStart;
    private double? _selectionEnd;
    private ReplayViewPoint? _selectedPoint;
    private DragMode _dragMode;
    private IReadOnlySet<string>? _enabledSeriesIds;
    private IReadOnlyDictionary<string, Color>? _seriesColors;
    private string? _selectedSeriesId;
    private ChartNumberFormatOptions _numberFormat = new();

    public InteractiveLineChart() { Focusable = true; Cursor = Cursors.Cross; }
    public event EventHandler<ChartSelectionStatistics?>? SelectionStatisticsChanged;

    public ChartNumberFormatOptions NumberFormat
    {
        get => _numberFormat;
        set { _numberFormat = value; InvalidateVisual(); }
    }

    public string FormatNumber(double value) => NumberFormat.UseScientificNotation
        ? value.ToString("G3", CultureInfo.CurrentCulture)
        : NumberFormat.UseThousandsSeparator
            ? value.ToString("#,0.###", CultureInfo.CurrentCulture)
            : value.ToString("0.###", CultureInfo.CurrentCulture);

    public IReadOnlySet<string>? EnabledSeriesIds
    {
        get => _enabledSeriesIds;
        set { _enabledSeriesIds = value; ResetDataRange(); }
    }

    public IReadOnlyDictionary<string, Color>? SeriesColors
    {
        get => _seriesColors;
        set { _seriesColors = value; InvalidateVisual(); }
    }

    public string? SelectedSeriesId
    {
        get => _selectedSeriesId;
        set
        {
            _selectedSeriesId = value;
            _selectedPoint = null;
            UpdateSelectionStatistics();
            InvalidateVisual();
        }
    }

    public ReplayView? Data
    {
        get => _data;
        set
        {
            _data = value;
            ResetDataRange();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(Brushes.White, null, new Rect(RenderSize));
        var series = VisibleSeries();
        var visible = series.SelectMany(item => item.Points).Where(point => point.X >= _viewMinX && point.X <= _viewMaxX).ToArray();
        if (visible.Length == 0) return;
        var activeSeries = ActiveSeries(series);

        const double left = 62, right = 18, top = 28, bottom = 46;
        var width = Math.Max(1, ActualWidth - left - right);
        var height = Math.Max(1, ActualHeight - top - bottom);
        var average = (activeSeries?.Points ?? visible).Average(point => point.Y);
        var minY = Math.Min(visible.Min(point => point.Y), average);
        var maxY = Math.Max(visible.Max(point => point.Y), average);
        var padding = Math.Max(1, (maxY - minY) * 0.12);
        minY = _dataMinY >= 0 ? Math.Max(0, minY - padding) : minY - padding;
        maxY += padding;
        if (maxY <= minY) maxY = minY + 1;
        if (_viewMinY is { } requestedMin && _viewMaxY is { } requestedMax)
        {
            minY = requestedMin;
            maxY = requestedMax;
        }
        else
        {
            minY = Math.Max(DataLowerLimit(), minY);
            maxY = Math.Min(DataUpperLimit(), maxY);
            if (maxY <= minY) maxY = minY + 1;
        }
        _renderMinY = minY;
        _renderMaxY = maxY;
        var plot = new Rect(left, top, width, height);

        var minor = new Pen(new SolidColorBrush(Color.FromRgb(235, 237, 240)), 1);
        var major = new Pen(new SolidColorBrush(Color.FromRgb(201, 205, 212)), 1);
        var axis = new Pen(new SolidColorBrush(Color.FromRgb(90, 96, 108)), 1);
        var xStep = NiceStep((_viewMaxX - _viewMinX) / Math.Max(2, width / 90));
        if (Data?.Metadata?.GetValueOrDefault("xInteger") is true) xStep = Math.Max(1, xStep);
        var yStep = NiceStep((maxY - minY) / Math.Max(2, height / 54));

        for (var value = Math.Ceiling(minY / yStep) * yStep; value <= maxY + yStep * 0.01; value += yStep)
        {
            var y = Y(value);
            dc.DrawLine(major, new Point(left, y), new Point(left + width, y));
            DrawText(dc, FormatNumber(value), 5, y - 8, 11, Brushes.DimGray);
            var half = value + yStep / 2;
            if (half < maxY) dc.DrawLine(minor, new Point(left, Y(half)), new Point(left + width, Y(half)));
        }
        for (var value = Math.Ceiling(_viewMinX / xStep) * xStep; value <= _viewMaxX + xStep * 0.01; value += xStep)
        {
            var x = X(value);
            dc.DrawLine(major, new Point(x, top), new Point(x, top + height));
            DrawText(dc, AxisText(value), x + 3, top + height + 7, 11, Brushes.DimGray);
            var half = value + xStep / 2;
            if (half < _viewMaxX) dc.DrawLine(minor, new Point(X(half), top), new Point(X(half), top + height));
        }

        dc.PushClip(new RectangleGeometry(plot));
        if (Data?.Metadata?.GetValueOrDefault("stageBoundaries") is IEnumerable<double> boundaries)
        {
            var boundaryPen = new Pen(Brushes.IndianRed, 1.5) { DashStyle = DashStyles.Dash };
            var labels = Data.Metadata.GetValueOrDefault("stageLabels") as IReadOnlyList<string>;
            var array = boundaries.ToArray();
            for (var index = 0; index < array.Length; index++)
            {
                var boundary = array[index];
                if (boundary <= _viewMinX || boundary >= _viewMaxX) continue;
                dc.DrawLine(boundaryPen, new Point(X(boundary), top), new Point(X(boundary), top + height));
                if (labels is not null && index + 1 < labels.Count) DrawText(dc, labels[index + 1], X(boundary) + 4, top + 3, 10, Brushes.IndianRed);
            }
        }

        var averagePen = new Pen(Brushes.SlateGray, 1) { DashStyle = DashStyles.Dash };
        dc.DrawLine(averagePen, new Point(left, Y(average)), new Point(left + width, Y(average)));
        DrawText(dc, $"{Convert.ToString(Data?.Metadata?.GetValueOrDefault("averageLabel"), CultureInfo.CurrentCulture)} {FormatNumber(average)}", left + 5, Y(average) - 17, 10, Brushes.SlateGray);

        if (_selectionStart is { } selectionStart && _selectionEnd is { } selectionEnd)
        {
            var x1 = X(Math.Min(selectionStart, selectionEnd));
            var x2 = X(Math.Max(selectionStart, selectionEnd));
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(42, 65, 105, 225)), null, new Rect(x1, top, Math.Max(1, x2 - x1), height));
            var selectionPen = new Pen(Brushes.RoyalBlue, 1.5);
            dc.DrawLine(selectionPen, new Point(x1, top), new Point(x1, top + height));
            dc.DrawLine(selectionPen, new Point(x2, top), new Point(x2, top + height));
        }

        var colors = new[] { Colors.RoyalBlue, Colors.OrangeRed, Colors.SeaGreen, Colors.MediumPurple, Colors.DarkGoldenrod };
        for (var seriesIndex = 0; seriesIndex < series.Count; seriesIndex++)
        {
            var points = series[seriesIndex].Points.Where(point => point.X >= _viewMinX && point.X <= _viewMaxX).ToArray();
            if (points.Length == 0) continue;
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(new Point(X(points[0].X), Y(points[0].Y)), false, false);
                for (var index = 1; index < points.Length; index++) context.LineTo(new Point(X(points[index].X), Y(points[index].Y)), true, false);
            }
            geometry.Freeze();
            var color = _seriesColors?.GetValueOrDefault(series[seriesIndex].Id) ?? colors[seriesIndex % colors.Length];
            var brush = new SolidColorBrush(color);
            var isActive = string.Equals(series[seriesIndex].Id, activeSeries?.Id, StringComparison.Ordinal);
            dc.DrawGeometry(null, new Pen(brush, isActive && series.Count > 1 ? 2.8 : 1.35), geometry);
            if (Data?.Metadata?.GetValueOrDefault("showAllAnchors") is true)
                foreach (var point in points)
                    dc.DrawEllipse(Brushes.White, new Pen(brush, isActive ? 1.8 : 1.2), new Point(X(point.X), Y(point.Y)), isActive ? 3.5 : 2.8, isActive ? 3.5 : 2.8);
            else if (Data?.Metadata?.GetValueOrDefault("showAnchors") is true)
                for (var index = 0; index < points.Length; index++)
                    if ((index > 0 && points[index].Y != points[index - 1].Y) ||
                        (index + 1 < points.Length && points[index].Y != points[index + 1].Y))
                        dc.DrawEllipse(Brushes.White, new Pen(brush, 1.2), new Point(X(points[index].X), Y(points[index].Y)), 2.8, 2.8);
        }
        if (_selectedPoint is { } selected)
        {
            var point = new Point(X(selected.X), Y(selected.Y));
            dc.DrawLine(new Pen(Brushes.DarkMagenta, 1), new Point(point.X, top), new Point(point.X, top + height));
            dc.DrawEllipse(Brushes.White, new Pen(Brushes.DarkMagenta, 2), point, 4, 4);
            var label = new FormattedText($"{AxisText(selected.X)}, {FormatNumber(selected.Y)}", CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, Brushes.DarkMagenta, 1);
            const double horizontalPadding = 4, verticalPadding = 2, anchorGap = 7;
            var boxWidth = label.Width + horizontalPadding * 2;
            var boxHeight = label.Height + verticalPadding * 2;
            var labelX = point.X + anchorGap;
            if (labelX + boxWidth > plot.Right) labelX = point.X - anchorGap - boxWidth;
            labelX = Math.Clamp(labelX, plot.Left + 2, Math.Max(plot.Left + 2, plot.Right - boxWidth - 2));
            var labelY = Math.Clamp(point.Y - boxHeight - 5, plot.Top + 2, Math.Max(plot.Top + 2, plot.Bottom - boxHeight - 2));
            var box = new Rect(labelX, labelY, boxWidth, boxHeight);
            dc.DrawRoundedRectangle(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(205, 207, 214)), 1), box, 2, 2);
            dc.DrawText(label, new Point(labelX + horizontalPadding, labelY + verticalPadding));
        }
        if (Data?.Metadata?.GetValueOrDefault("stageLabels") is IReadOnlyList<string> stageLabels && stageLabels.Count > 0)
            DrawText(dc, stageLabels[0], X(_dataMinX) + 4, top + 3, 10, Brushes.IndianRed);
        dc.Pop();

        dc.DrawLine(axis, plot.TopLeft, plot.BottomLeft);
        dc.DrawLine(axis, plot.BottomLeft, plot.BottomRight);
        DrawText(dc, Convert.ToString(Data?.Metadata?.GetValueOrDefault("yUnit"), CultureInfo.CurrentCulture) ?? "", 6, 4, 11, Brushes.DimGray);
        DrawText(dc, Convert.ToString(Data?.Metadata?.GetValueOrDefault("xUnit"), CultureInfo.CurrentCulture) ?? "", Math.Max(left, left + width - 60), top + height + 25, 11, Brushes.DimGray);
        DrawText(dc, Convert.ToString(Data?.Metadata?.GetValueOrDefault("interactionHint"), CultureInfo.CurrentCulture) ?? "", left + 8, 5, 11, Brushes.Gray);
        double X(double value) => left + width * (value - _viewMinX) / Math.Max(0.000001, _viewMaxX - _viewMinX);
        double Y(double value) => top + height * (maxY - value) / Math.Max(0.000001, maxY - minY);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (_dataMaxX <= _dataMinX) return;
        var anchor = ValueAt(e.GetPosition(this).X);
        var span = _viewMaxX - _viewMinX;
        var minimum = Math.Max((_dataMaxX - _dataMinX) / 2000, 0.5);
        var requested = Math.Clamp(span * (e.Delta > 0 ? 0.8 : 1.25), minimum, _dataMaxX - _dataMinX);
        var ratio = (anchor - _viewMinX) / Math.Max(0.000001, span);
        SetView(anchor - requested * ratio, anchor + requested * (1 - ratio));
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var position = e.GetPosition(this);
        Focus();
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0)
        {
            _selectedPoint = NearestPoint(ValueAt(position.X));
            _dragStart = position;
            _dragMode = DragMode.Point;
            CaptureMouse();
            InvalidateVisual(); e.Handled = true; return;
        }
        _dragStart = position; _dragViewStart = _viewMinX; _dragViewEnd = _viewMaxX;
        _dragViewMinY = _renderMinY; _dragViewMaxY = _renderMaxY;
        _dragMode = (Keyboard.Modifiers & ModifierKeys.Control) != 0 ? DragMode.Pan : DragMode.Selection;
        if (_dragMode == DragMode.Selection)
        {
            _selectionStart = _selectionEnd = ValueAt(position.X);
            UpdateSelectionStatistics();
        }
        CaptureMouse(); e.Handled = true; InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        Cursor = (Keyboard.Modifiers & ModifierKeys.Control) != 0 ? Cursors.Hand : Cursors.Cross;
        if (_dragStart is null || !IsMouseCaptured) return;
        if (_dragMode == DragMode.Point)
            _selectedPoint = NearestPoint(ValueAt(e.GetPosition(this).X));
        else if (_dragMode == DragMode.Pan)
        {
            var position = e.GetPosition(this);
            var width = Math.Max(1, ActualWidth - 80);
            var height = Math.Max(1, ActualHeight - 74);
            var deltaX = (position.X - _dragStart.Value.X) / width * (_dragViewEnd - _dragViewStart);
            var deltaY = (position.Y - _dragStart.Value.Y) / height * (_dragViewMaxY - _dragViewMinY);
            SetView(_dragViewStart - deltaX, _dragViewEnd - deltaX);
            SetYView(_dragViewMinY + deltaY, _dragViewMaxY + deltaY);
        }
        else
        {
            _selectionEnd = ValueAt(e.GetPosition(this).X);
            UpdateSelectionStatistics();
        }
        e.Handled = true; InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (IsMouseCaptured) ReleaseMouseCapture(); _dragStart = null; _dragMode = DragMode.None; e.Handled = true; InvalidateVisual();
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e); Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.D && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            _selectionStart = _selectionEnd = null;
            _selectedPoint = null;
            SelectionStatisticsChanged?.Invoke(this, null);
            InvalidateVisual(); e.Handled = true; return;
        }
        base.OnKeyDown(e);
    }

    private double ValueAt(double x)
    {
        const double left = 62, right = 18;
        return _viewMinX + Math.Clamp((x - left) / Math.Max(1, ActualWidth - left - right), 0, 1) * (_viewMaxX - _viewMinX);
    }

    private void SetView(double start, double end)
    {
        var span = Math.Min(_dataMaxX - _dataMinX, end - start);
        if (start < _dataMinX) { start = _dataMinX; end = start + span; }
        if (end > _dataMaxX) { end = _dataMaxX; start = end - span; }
        _viewMinX = Math.Max(_dataMinX, start); _viewMaxX = Math.Min(_dataMaxX, end); InvalidateVisual();
    }

    private void SetYView(double start, double end)
    {
        var lowerLimit = DataLowerLimit();
        var upperLimit = DataUpperLimit();
        if (upperLimit <= lowerLimit) upperLimit = lowerLimit + 1;
        var span = Math.Min(end - start, upperLimit - lowerLimit);
        if (start < lowerLimit) { start = lowerLimit; end = start + span; }
        if (end > upperLimit) { end = upperLimit; start = end - span; }
        _viewMinY = Math.Max(lowerLimit, start);
        _viewMaxY = Math.Min(upperLimit, end);
    }

    private double DataLowerLimit() => _dataMinY < 0 ? _dataMinY * 2 : 0;
    private double DataUpperLimit()
    {
        var value = _dataMaxY > 0 ? _dataMaxY * 2 : 0;
        return value > DataLowerLimit() ? value : DataLowerLimit() + 1;
    }

    private static double NiceStep(double raw)
    {
        if (raw <= 0 || double.IsNaN(raw)) return 1;
        var power = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var normalized = raw / power;
        return (normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10) * power;
    }

    private void UpdateSelectionStatistics()
    {
        if (_selectionStart is not { } start || _selectionEnd is not { } end)
        {
            SelectionStatisticsChanged?.Invoke(this, null);
            return;
        }
        var minimumX = Math.Min(start, end);
        var maximumX = Math.Max(start, end);
        var values = ActiveSeries(VisibleSeries())?.Points.Where(point => point.X >= minimumX && point.X <= maximumX).Select(point => point.Y).ToArray() ?? [];
        if (values.Length == 0)
        {
            SelectionStatisticsChanged?.Invoke(this, null);
            return;
        }
        var average = values.Average();
        var variance = values.Average(value => (value - average) * (value - average));
        SelectionStatisticsChanged?.Invoke(this, new ChartSelectionStatistics(minimumX, maximumX, values.Length, average, variance, values.Min(), values.Max()));
    }

    private ReplayViewPoint? NearestPoint(double x) => ActiveSeries(VisibleSeries())?.Points.MinBy(point => Math.Abs(point.X - x));
    private IReadOnlyList<ReplayViewSeries> VisibleSeries() => Data?.Series?
        .Where(series => _enabledSeriesIds is null || _enabledSeriesIds.Contains(series.Id)).ToArray() ?? [];
    private ReplayViewSeries? ActiveSeries(IReadOnlyList<ReplayViewSeries> series) =>
        series.FirstOrDefault(item => string.Equals(item.Id, _selectedSeriesId, StringComparison.Ordinal)) ?? series.FirstOrDefault();

    private void ResetDataRange()
    {
        var points = VisibleSeries().SelectMany(series => series.Points).ToArray();
        _dataMinX = points.Length == 0 ? 0 : points.Min(point => point.X);
        _dataMaxX = points.Length == 0 ? 1 : Math.Max(_dataMinX + 0.001, points.Max(point => point.X));
        _dataMinY = points.Length == 0 ? 0 : points.Min(point => point.Y);
        _dataMaxY = points.Length == 0 ? 1 : points.Max(point => point.Y);
        _viewMinX = _dataMinX;
        _viewMaxX = _dataMaxX;
        _viewMinY = _viewMaxY = null;
        _selectionStart = _selectionEnd = null;
        _selectedPoint = null;
        SelectionStatisticsChanged?.Invoke(this, null);
        InvalidateVisual();
    }
    private string AxisText(double value) => Data?.Metadata?.GetValueOrDefault("xIsFrame") is true
        ? RepViewer.Core.ReplayFrameTime.Format(checked((int)Math.Round(value)))
        : Data?.Metadata?.GetValueOrDefault("xInteger") is true
            ? value.ToString("0", CultureInfo.CurrentCulture)
        : value.ToString("0.##", CultureInfo.CurrentCulture);
    private static void DrawText(DrawingContext dc, string text, double x, double y, double size, Brush brush) =>
        dc.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, 1), new Point(x, y));

    private enum DragMode { None, Selection, Pan, Point }
}
