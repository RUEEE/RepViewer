using System.Buffers.Binary;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using RepViewer.Core;
using RepViewer.Presentation;

namespace RepViewer.App;

internal sealed class CommentDialog : Window
{
    private readonly TextBox _text;
    private readonly ComboBox _encoding;
    public CommentDialog(string comment, int encodingIndex, Func<int, string> decode, PresentationCatalog presentation)
    {
        Title = presentation.Text("dialog.comment.title"); Width = 560; Height = 390; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(12) };
        var bottom = new Grid { Margin = new Thickness(0, 18, 0, 2) };
        bottom.ColumnDefinitions.Add(new ColumnDefinition()); bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var encodingPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        encodingPanel.Children.Add(new TextBlock { Text = presentation.Text("settings.commentEncoding"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) });
        _encoding = new ComboBox { ItemsSource = new[] { "Shift-JIS", "UTF-8", "ANSI" }, SelectedIndex = encodingIndex, Width = 110 };
        encodingPanel.Children.Add(_encoding); bottom.Children.Add(encodingPanel);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = presentation.Text("command.ok"), IsDefault = true }; ok.Click += (_, _) => DialogResult = true;
        var cancel = new Button { Content = presentation.Text("command.cancel"), IsCancel = true }; buttons.Children.Add(ok); buttons.Children.Add(cancel); Grid.SetColumn(buttons, 1); bottom.Children.Add(buttons);
        DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        _text = new TextBox { Text = comment, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(5) };
        _encoding.SelectionChanged += (_, _) => _text.Text = decode(_encoding.SelectedIndex);
        root.Children.Add(_text); Content = root;
    }
    public string Comment => _text.Text;
    public int EncodingIndex => _encoding.SelectedIndex;
}

internal sealed class SettingsDialog : Window
{
    private readonly ComboBox _locale;
    private readonly List<CheckBox> _plugins = [];
    private readonly CheckBox _association;
    private readonly CheckBox _chartScientific;
    private readonly CheckBox _chartThousands;
    private readonly ComboBox _uiScale;
    public SettingsDialog(string locale, PresentationCatalog presentation, IEnumerable<string> pluginIds, IReadOnlySet<string> enabledPlugins,
        bool fileAssociated, bool chartUseScientificNotation, bool chartUseThousandsSeparator, int uiScalePercent)
    {
        Title = presentation.Text("command.settings"); Width = 440; Height = 650; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock { Text = presentation.Text("settings.language"), Margin = new Thickness(0, 0, 0, 6) });
        _locale = new ComboBox { ItemsSource = new[] { "zh-CN", "en-US" }, SelectedItem = locale, Margin = new Thickness(0, 0, 0, 12) }; root.Children.Add(_locale);
        root.Children.Add(new TextBlock { Text = presentation.Text("settings.interfaceScale"), Margin = new Thickness(0, 0, 0, 6) });
        _uiScale = new ComboBox { ItemsSource = new[] { "100%", "125%", "150%", "175%", "200%" }, SelectedItem = $"{uiScalePercent}%", Margin = new Thickness(0, 0, 0, 16) };
        root.Children.Add(_uiScale);
        _association = new CheckBox { Content = presentation.Text("settings.fileAssociation"), IsChecked = fileAssociated, Margin = new Thickness(2, 0, 0, 3) };
        root.Children.Add(_association);
        root.Children.Add(new TextBlock { Text = presentation.Text("settings.fileAssociationHint"), TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.DimGray, Margin = new Thickness(22, 0, 0, 15) });
        root.Children.Add(new TextBlock { Text = presentation.Text("settings.chartNumberFormat"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        _chartScientific = new CheckBox { Content = presentation.Text("settings.chartScientificNotation"), IsChecked = chartUseScientificNotation, Margin = new Thickness(2, 3, 0, 4) };
        _chartThousands = new CheckBox { Content = presentation.Text("settings.chartThousandsSeparator"), IsChecked = chartUseThousandsSeparator, Margin = new Thickness(2, 3, 0, 15) };
        root.Children.Add(_chartScientific);
        root.Children.Add(_chartThousands);
        root.Children.Add(new TextBlock { Text = presentation.Text("settings.plugins"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        foreach (var id in pluginIds)
        {
            var check = new CheckBox { Content = presentation.Text($"plugin.{id}"), Tag = id, IsChecked = enabledPlugins.Contains(id), Margin = new Thickness(2, 4, 0, 4) };
            _plugins.Add(check); root.Children.Add(check);
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = presentation.Text("command.ok"), IsDefault = true }; ok.Click += (_, _) => DialogResult = true; buttons.Children.Add(ok); buttons.Children.Add(new Button { Content = presentation.Text("command.cancel"), IsCancel = true }); root.Children.Add(buttons); Content = root;
    }
    public string Locale => (string?)_locale.SelectedItem ?? "zh-CN";
    public bool FileAssociated => _association.IsChecked == true;
    public bool ChartUseScientificNotation => _chartScientific.IsChecked == true;
    public bool ChartUseThousandsSeparator => _chartThousands.IsChecked == true;
    public int UiScalePercent => int.TryParse(Convert.ToString(_uiScale.SelectedItem)?.TrimEnd('%'), out var value) ? value : 100;
    public IReadOnlyList<string> EnabledPluginIds => _plugins.Where(check => check.IsChecked == true).Select(check => (string)check.Tag).ToArray();
}

internal enum AssociationPromptResult { Yes, No, Never }

internal sealed class AssociationPromptDialog : Window
{
    public AssociationPromptResult Result { get; private set; } = AssociationPromptResult.No;

    public AssociationPromptDialog()
    {
        Title = "RepViewer"; Width = 430; Height = 175; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        var root = new DockPanel { Margin = new Thickness(18), LastChildFill = false };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        AddButton(buttons, "是", AssociationPromptResult.Yes, true);
        AddButton(buttons, "否", AssociationPromptResult.No, false);
        AddButton(buttons, "否且不再提示", AssociationPromptResult.Never, false);
        DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons); Content = root;
        var question = new TextBlock { Text = "是否关联rpy文件", FontSize = 16, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(question, Dock.Top); root.Children.Add(question);
    }

    private void AddButton(Panel panel, string text, AssociationPromptResult result, bool isDefault)
    {
        var button = new Button { Content = text, MinWidth = result == AssociationPromptResult.Never ? 112 : 68, Margin = new Thickness(6, 0, 0, 0), IsDefault = isDefault };
        button.Click += (_, _) => { Result = result; DialogResult = true; };
        panel.Children.Add(button);
    }
}

internal sealed class PunishmentDialog : Window
{
    private static readonly string[] Labels = ["↙", "↓", "↘", "←", "∅", "→", "↖", "↑", "↗"];
    private readonly TextBox?[,] _values = new TextBox?[9, 9];

    public PunishmentDialog(DirectionPunishmentTable table, PresentationCatalog presentation)
    {
        Title = presentation.Text("command.punishment"); Width = 720; Height = 510; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(12) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var reset = new Button { Content = presentation.Text("command.reset") }; reset.Click += (_, _) => Load(DirectionPunishmentTable.CreateDefault());
        var ok = new Button { Content = presentation.Text("command.ok"), IsDefault = true }; ok.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(reset); buttons.Children.Add(ok); buttons.Children.Add(new Button { Content = presentation.Text("command.cancel"), IsCancel = true });
        DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        var matrix = new Grid();
        for (var index = 0; index < 10; index++) { matrix.RowDefinitions.Add(new RowDefinition()); matrix.ColumnDefinitions.Add(new ColumnDefinition()); }
        for (var index = 0; index < 9; index++)
        {
            AddCell(matrix, new TextBlock { Text = Labels[index], HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold }, 0, index + 1);
            AddCell(matrix, new TextBlock { Text = Labels[index], HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold }, index + 1, 0);
        }
        for (var row = 0; row < 9; row++)
            for (var column = row; column < 9; column++)
            {
                var editor = new TextBox { HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(3) };
                _values[row, column] = editor; AddCell(matrix, editor, row + 1, column + 1);
            }
        root.Children.Add(matrix); Content = root; Load(table);
    }

    private void Load(DirectionPunishmentTable table)
    {
        for (var row = 0; row < 9; row++)
            for (var column = row; column < 9; column++)
                _values[row, column]!.Text = table[row + 1, column + 1].ToString("0.###", CultureInfo.InvariantCulture);
    }

    public void ApplyTo(DirectionPunishmentTable table)
    {
        for (var row = 1; row <= 9; row++)
            for (var column = row; column <= 9; column++)
                if (float.TryParse(_values[row - 1, column - 1]!.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) table[row, column] = value;
    }

    private static void AddCell(Grid grid, FrameworkElement content, int row, int column)
    {
        var border = new Border { BorderBrush = System.Windows.Media.Brushes.LightGray, BorderThickness = new Thickness(0.5), Child = content };
        Grid.SetRow(border, row); Grid.SetColumn(border, column); grid.Children.Add(border);
    }
}

internal sealed class UnknownDataDialog : Window
{
    public UnknownDataDialog(UnknownField? unknown, PresentationCatalog presentation)
    {
        Title = presentation.Text("dialog.unknown.title"); Width = 940; Height = 560; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var tabs = new TabControl { Margin = new Thickness(10) };
        var hiddenCount = unknown?.KnownValues.Count ?? 0;
        var unknownCount = unknown?.UnknownValues.Count ?? 0;
        var hidden = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false };
        hidden.Columns.Add(Column(presentation.Text("column.field"), nameof(HiddenRow.Field), 150));
        hidden.Columns.Add(Column(presentation.Text("column.rawField"), nameof(HiddenRow.RawField), 210));
        hidden.Columns.Add(Column(presentation.Text("column.value"), nameof(HiddenRow.Value), 230));
        hidden.Columns.Add(Column(presentation.Text("column.raw"), nameof(HiddenRow.RawDisplay), 120));
        hidden.ItemsSource = unknown?.KnownValues.Select(pair =>
        {
            var raw = pair.Value is SemanticField semantic ? semantic.RawValue : pair.Value;
            var source = pair.Value is SemanticField sourceField ? sourceField.SourceFieldName ?? pair.Key : pair.Key;
            var offset = pair.Value is SemanticField offsetField ? offsetField.Offset : null;
            return new HiddenRow(pair.Key, offset is { } value ? $"{source}（+0x{value:X}）" : source,
                pair.Value is SemanticField field ? Convert.ToString(field.Value, CultureInfo.CurrentCulture) ?? "" : Convert.ToString(pair.Value, CultureInfo.CurrentCulture) ?? "",
                RawBytes(raw));
        }).DefaultIfEmpty(new HiddenRow(presentation.Text("value.empty"), "", "", [])).ToArray()
            ?? [new HiddenRow(presentation.Text("value.empty"), "", "", [])];
        DataGridFeatures.EnableCopy(hidden, presentation);
        hidden.MouseDoubleClick += (_, _) => { if (hidden.SelectedItem is HiddenRow { Data.Length: > 0 } row) new RawDataDialog(row.Field, row.Data, presentation) { Owner = this }.ShowDialog(); };
        tabs.Items.Add(new TabItem { Header = $"{presentation.Text("section.hidden")} ({hiddenCount})", Content = hidden });

        var raw = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false };
        foreach (var (header, path, width) in new[] { (presentation.Text("column.field"), nameof(UnknownRow.Name), 120), (presentation.Text("column.offset"), nameof(UnknownRow.Offset), 85),
            ("Hex", nameof(UnknownRow.Hex), 140), ("Int8", nameof(UnknownRow.Int8), 125), ("UInt8", nameof(UnknownRow.UInt8), 125), ("Int16", nameof(UnknownRow.Int16), 135),
            ("UInt16", nameof(UnknownRow.UInt16), 135), ("Int32", nameof(UnknownRow.Int32), 105), ("UInt32", nameof(UnknownRow.UInt32), 105), ("Float", nameof(UnknownRow.Float), 125) })
            raw.Columns.Add(Column(header, path, width));
        raw.ItemsSource = unknown?.UnknownValues.Select(value => UnknownRow.From(value)).DefaultIfEmpty(UnknownRow.Empty(presentation.Text("value.empty"))).ToArray()
            ?? [UnknownRow.Empty(presentation.Text("value.empty"))];
        DataGridFeatures.EnableCopy(raw, presentation);
        tabs.Items.Add(new TabItem { Header = $"{presentation.Text("section.unknown")} ({unknownCount})", Content = raw });
        tabs.SelectedIndex = hiddenCount > 0 || unknownCount == 0 ? 0 : 1;
        Content = tabs;
    }

    private static DataGridTextColumn Column(string header, string path, double width) => new() { Header = header, Binding = new System.Windows.Data.Binding(path), Width = width, CanUserSort = false };
    private sealed record HiddenRow(string Field, string RawField, string Value, byte[] Data) { public string RawDisplay => Data.Length == 0 ? "" : "..."; }
    private sealed record UnknownRow(string Name, string Offset, string Hex, string Int8, string UInt8, string Int16, string UInt16, string Int32, string UInt32, string Float)
    {
        public static UnknownRow Empty(string text) => new(text, "", "", "", "", "", "", "", "", "");
        public static UnknownRow From(UnknownValue value)
        {
            var data = value.Data;
            Span<byte> four = stackalloc byte[4]; data.AsSpan(0, Math.Min(4, data.Length)).CopyTo(four);
            var u8 = string.Join(", ", data.Select(item => item.ToString(CultureInfo.InvariantCulture)));
            var u16 = new List<string>();
            for (var index = 0; index < data.Length; index += 2) { Span<byte> two = new byte[2]; data.AsSpan(index, Math.Min(2, data.Length - index)).CopyTo(two); u16.Add(BinaryPrimitives.ReadUInt16LittleEndian(two).ToString(CultureInfo.InvariantCulture)); }
            return new(value.Name, $"+0x{value.Offset:X}", value.Hex, value.Int8, u8, value.Int16, string.Join(", ", u16), value.Int32,
                BinaryPrimitives.ReadUInt32LittleEndian(four).ToString(CultureInfo.InvariantCulture), value.Float);
        }
    }

    private static byte[] RawBytes(object? value) => value switch
    {
        null => [], byte[] bytes => bytes, IEnumerable<byte> bytes => bytes.ToArray(),
        byte number => [number], sbyte number => [unchecked((byte)number)],
        short number => BitConverter.GetBytes(number), ushort number => BitConverter.GetBytes(number),
        int number => BitConverter.GetBytes(number), uint number => BitConverter.GetBytes(number),
        long number => BitConverter.GetBytes(number), ulong number => BitConverter.GetBytes(number),
        float number => BitConverter.GetBytes(number), double number => BitConverter.GetBytes(number),
        _ => System.Text.Encoding.UTF8.GetBytes(DisplayFormatter.FormatRaw(value))
    };
}

internal sealed class ExtensionDataDialog : Window
{
    public ExtensionDataDialog(ReplayDocument? replay, PresentationCatalog presentation)
    {
        Title = presentation.Text("dialog.extension.title"); Width = 780; Height = 480; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var rows = new List<ExtensionRow>();
        if (replay?.ModMetadata is { } mod) rows.Add(new ExtensionRow("PRAC", mod.Offset, mod.RawData.Length, mod.Text ?? presentation.Text("value.binary"), mod.RawData));
        if (replay?.UserData is { } userData)
        {
            var offset = userData.Offset;
            foreach (var block in userData.Blocks)
            {
                rows.Add(new ExtensionRow($"{block.Marker} #{block.Id}", offset + 12, block.Data.Length, "...", block.Data));
                offset += block.EncodedLength;
            }
        }
        if (rows.Count == 0) rows.Add(new ExtensionRow(presentation.Text("value.empty"), 0, 0, "", []));
        var grid = new DataGrid { ItemsSource = rows, IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, Margin = new Thickness(10) };
        grid.Columns.Add(Column(presentation.Text("column.field"), nameof(ExtensionRow.Name), 170));
        grid.Columns.Add(Column(presentation.Text("column.offset"), nameof(ExtensionRow.OffsetText), 100));
        grid.Columns.Add(Column("Bytes", nameof(ExtensionRow.Length), 80));
        grid.Columns.Add(Column(presentation.Text("column.value"), nameof(ExtensionRow.Value), 340));
        DataGridFeatures.EnableCopy(grid, presentation);
        grid.MouseDoubleClick += (_, _) => { if (grid.SelectedItem is ExtensionRow { Data.Length: > 0 } row) new RawDataDialog(row.Name, row.Data, presentation) { Owner = this }.ShowDialog(); };
        Content = grid;
    }
    private static DataGridTextColumn Column(string header, string path, double width) => new() { Header = header, Binding = new System.Windows.Data.Binding(path), Width = width, CanUserSort = false };
    private sealed record ExtensionRow(string Name, int Offset, int Length, string Value, byte[] Data) { public string OffsetText => $"0x{Offset:X}"; }
}

internal sealed class RawDataDialog : Window
{
    public RawDataDialog(string name, byte[] data, PresentationCatalog presentation)
    {
        Title = $"{presentation.Text("dialog.raw.title")} — {name}"; Width = 820; Height = 560; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var hex = string.Join(Environment.NewLine, Enumerable.Range(0, (data.Length + 15) / 16).Select(line =>
        {
            var offset = line * 16; var block = data.AsSpan(offset, Math.Min(16, data.Length - offset)).ToArray();
            return $"{offset:X8}  {string.Join(' ', block.Select(value => value.ToString("X2")))}";
        }));
        var tabs = new TabControl { Margin = new Thickness(10) };
        tabs.Items.Add(new TabItem { Header = "Hex", Content = Text(hex, false) });
        tabs.Items.Add(new TabItem { Header = "Shift-JIS", Content = Text(ReplayTextEncoding.ShiftJis.GetString(data), true) });
        tabs.Items.Add(new TabItem { Header = "UTF-8", Content = Text(System.Text.Encoding.UTF8.GetString(data), true) });
        Content = tabs;
    }
    private static TextBox Text(string value, bool wrap) => new() { Text = value, IsReadOnly = true, FontFamily = new System.Windows.Media.FontFamily("Consolas"), TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
}

internal sealed class SpellTimesDialog : Window
{
    public SpellTimesDialog(IEnumerable<ReplaySpellTime> values, PresentationCatalog presentation)
    {
        Title = presentation.Field("SpellTimes").Label; Width = 430; Height = 520; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var grid = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, Margin = new Thickness(10) };
        grid.Columns.Add(new DataGridTextColumn { Header = "#", Binding = new System.Windows.Data.Binding(nameof(SpellTimeRow.Index)), Width = 70, CanUserSort = false });
        grid.Columns.Add(new DataGridTextColumn { Header = presentation.Text("field.spellTimeSeconds"), Binding = new System.Windows.Data.Binding(nameof(SpellTimeRow.Time)), Width = new DataGridLength(1, DataGridLengthUnitType.Star), CanUserSort = false });
        grid.ItemsSource = values.Select((value, index) => new SpellTimeRow(index + 1, $"{value.Seconds.ToString("0.00", CultureInfo.InvariantCulture)}s")).ToArray();
        DataGridFeatures.EnableCopy(grid, presentation); Content = grid;
    }
    private sealed record SpellTimeRow(int Index, string Time);
}

internal sealed class CardsDialog : Window
{
    public CardsDialog(IEnumerable<ReplayCard> values, FieldPresentation field, PresentationCatalog presentation)
    {
        Title = field.Label; Width = 720; Height = 540; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var grid = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, Margin = new Thickness(10) };
        grid.Columns.Add(new DataGridTextColumn { Header = "#", Binding = new System.Windows.Data.Binding(nameof(CardRow.Index)), Width = 60, CanUserSort = false });
        grid.Columns.Add(new DataGridTextColumn { Header = presentation.Text("field.cardName"), Binding = new System.Windows.Data.Binding(nameof(CardRow.Name)), Width = new DataGridLength(1, DataGridLengthUnitType.Star), CanUserSort = false });
        grid.Columns.Add(new DataGridTextColumn { Header = presentation.Text("field.cardId"), Binding = new System.Windows.Data.Binding(nameof(CardRow.Id)), Width = 110, CanUserSort = false });
        grid.Columns.Add(new DataGridTextColumn { Header = presentation.Text("field.cooldown"), Binding = new System.Windows.Data.Binding(nameof(CardRow.Cooldown)), Width = 140, CanUserSort = false });
        grid.ItemsSource = values.Select((value, index) => new CardRow(index + 1,
            field.Values?.GetValueOrDefault(value.CardId.ToString(CultureInfo.InvariantCulture)) ?? value.CardId.ToString(CultureInfo.InvariantCulture),
            value.CardId, ReplayFrameTime.Format(value.CooldownFrames))).ToArray();
        DataGridFeatures.EnableCopy(grid, presentation); Content = grid;
    }
    private sealed record CardRow(int Index, string Name, int Id, string Cooldown);
}
