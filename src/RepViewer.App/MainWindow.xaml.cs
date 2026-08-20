using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using RepViewer.Core;
using RepViewer.Plugins;
using RepViewer.Presentation;

namespace RepViewer.App;

public partial class MainWindow : Window
{
    private static readonly string ApplicationVersion = typeof(MainWindow).Assembly.GetName().Version?.ToString(4) ?? "1.1.0.0";
    private readonly ReplayPluginHost _plugins = new();
    private readonly DirectionPunishmentTable _punishment = DirectionPunishmentTable.CreateDefault();
    private HashSet<string> _enabledPlugins;
    private ReplayDocument? _replay;
    private string? _path;
    private bool _hasUnsavedChanges;
    private bool _chartUseScientificNotation;
    private bool _chartUseThousandsSeparator = true;
    private int _uiScalePercent = 100;
    private string _locale = "zh-CN";
    private PresentationCatalog _presentation;

    public MainWindow()
    {
        var settings = AppSettingsStore.Load();
        _locale = settings.Locale is "zh-CN" or "en-US" ? settings.Locale : DefaultLocale();
        _chartUseScientificNotation = settings.ChartUseScientificNotation;
        _chartUseThousandsSeparator = settings.ChartUseThousandsSeparator;
        _uiScalePercent = settings.UiScalePercent is 100 or 125 or 150 or 175 or 200 ? settings.UiScalePercent : 100;
        UiScaleService.SetScale(_uiScalePercent / 100d);
        InitializeComponent();
        _presentation = PresentationCatalog.Load(PresentationCatalog.DefaultRoot, _locale, "th06");
        var availablePlugins = _plugins.Plugins.Select(plugin => plugin.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _enabledPlugins = settings.EnabledPluginIds is null
            ? availablePlugins
            : settings.EnabledPluginIds.Where(availablePlugins.Contains).ToHashSet(StringComparer.OrdinalIgnoreCase);
        EncodingCombo.ItemsSource = new[] { "Shift-JIS", "UTF-8", "ANSI" };
        EncodingCombo.SelectedIndex = 0;
        ApplyLanguage();
        StatusText.Text = _presentation.Text("status.ready");
    }

    private void OpenClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Touhou replay (*.rpy)|*.rpy|All files (*.*)|*.*" };
        if (dialog.ShowDialog(this) != true) return;
        if (!ConfirmDiscardChanges()) return;
        OpenReplay(dialog.FileName);
    }

    public bool OpenReplay(string path, bool showErrors = true)
    {
        try
        {
            var replay = ReplayApi.ReadFile(path);
            _path = path;
            _replay = replay;
            _hasUnsavedChanges = false;
            _presentation = PresentationCatalog.Load(PresentationCatalog.DefaultRoot, _locale, _replay.Identity.GameId);
            FileText.Text = _path;
            ExportButton.IsEnabled = true;
            PopulateReplayTabs();
            StatusText.Text = $"{_replay.Identity.FormatId} · {_replay.Stages.Count} {_presentation.Text("unit.stages")}";
            ApplyLanguage();
            return true;
        }
        catch (Exception exception)
        {
            if (showErrors) MessageBox.Show(this, exception.Message, "RepViewer", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void WindowDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DroppedReplay(e.Data) is not null ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void WindowDrop(object sender, DragEventArgs e)
    {
        var path = DroppedReplay(e.Data);
        if (path is not null && ConfirmDiscardChanges()) OpenReplay(path);
        e.Handled = true;
    }

    private static string? DroppedReplay(IDataObject data)
    {
        if (!data.GetDataPresent(DataFormats.FileDrop) || data.GetData(DataFormats.FileDrop) is not string[] { Length: 1 } files) return null;
        var path = files[0];
        return File.Exists(path) && Path.GetExtension(path).Equals(".rpy", StringComparison.OrdinalIgnoreCase) ? path : null;
    }

    private bool ConfirmDiscardChanges()
    {
        if (!_hasUnsavedChanges) return true;
        return MessageBox.Show(this, "当前录像包含未保存的修改。是否放弃修改并打开另一个录像？", "RepViewer",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private void PopulateReplayTabs()
    {
        ReplayTabs.Items.Clear();
        ReplayTabs.Items.Add(new TabItem { Header = _presentation.Text("section.general"), Tag = -1 });
        for (var index = 0; index < _replay!.Stages.Count; index++)
            ReplayTabs.Items.Add(new TabItem
            {
                Header = $"{_presentation.Text("section.stage")} {_replay.Stages[index].StageId}",
                Tag = index
            });
        ReplayTabs.SelectedIndex = 0;
        EnsureSelectedScope();
    }

    private void ReplayTabsChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, ReplayTabs)) return;
        EnsureSelectedScope();
    }

    private void EnsureSelectedScope()
    {
        if (_replay is null || ReplayTabs.SelectedItem is not TabItem { Tag: int scope } tab || tab.Content is not null) return;
        tab.Content = CreateScopePanel(scope < 0 ? null : scope);
    }

    private TabControl CreateScopePanel(int? stageIndex)
    {
        var tabs = new TabControl { BorderThickness = new Thickness(0), Margin = new Thickness(4) };
        var host = new ReplayPluginHost(_plugins.Plugins.Where(plugin => _enabledPlugins.Contains(plugin.Id)));
        foreach (var view in host.CreateViews(new ReplayPluginContext(_replay!, _presentation, stageIndex, _punishment)))
            tabs.Items.Add(new TabItem { Header = _presentation.Text(view.Title), Tag = view.Id, Content = CreateView(view) });
        return tabs;
    }

    private FrameworkElement CreateView(ReplayView view) => view.Kind switch
    {
        _ when view.Id == "overview" => CreatePropertyPanel(view),
        _ when view.Id == "key-list" => new AdaptiveFrameGrid(view, _presentation) { Margin = new Thickness(8) },
        _ when view.Id == "fps-list" => new AdaptiveFpsGrid(view, _presentation) { Margin = new Thickness(8) },
        _ when view.Id == "key-rates" => new KeyRateChartStack(view, _presentation, EditPunishment, ChartNumberFormat) { Margin = new Thickness(6) },
        ReplayViewKind.HeatMap => new TransitionStatisticsPanel(view, _presentation),
        ReplayViewKind.LineChart => new LineChartPanel(view, _presentation, ChartNumberFormat) { Margin = new Thickness(8), MinHeight = 320 },
        _ => CreateTable(view)
    };

    private DataGrid CreateTable(ReplayView view)
    {
        var grid = new DataGrid { AutoGenerateColumns = false, Margin = new Thickness(8) };
        foreach (var column in view.Columns ?? [])
        {
            var index = grid.Columns.Count;
            grid.Columns.Add(new DataGridTextColumn { Header = column.Label, Binding = new System.Windows.Data.Binding($"[{index}]"), CanUserSort = false });
        }
        grid.ItemsSource = (view.Rows ?? []).Select(row => row.Cells.ToArray()).ToArray();
        DataGridFeatures.EnableCopy(grid, _presentation);
        return grid;
    }

    private FrameworkElement CreatePropertyPanel(ReplayView view)
    {
        var propertyRows = (view.Rows ?? []).Select(row => new PropertyUiRow(row.Id,
            Convert.ToString(row.Cells.ElementAtOrDefault(0)) ?? "", Convert.ToString(row.Cells.ElementAtOrDefault(1)) ?? "",
            Convert.ToString(row.Cells.ElementAtOrDefault(2)) ?? "", Convert.ToString(row.Cells.ElementAtOrDefault(3)) ?? "")).ToList();
        var isGeneral = view.Metadata?.GetValueOrDefault("isGeneral") is true;
        if (isGeneral) propertyRows.Add(new PropertyUiRow("Comment", _presentation.Text("field.comment"), "Comment", DecodeComment(), ""));
        propertyRows.Add(new PropertyUiRow("__unknown", _presentation.Text("section.unknownHidden"), "unknown", "...", "..."));
        propertyRows.Add(new PropertyUiRow("__mod", _presentation.Text("section.modData"), "USER / Mod", "...", "..."));
        var properties = PropertyGrid(propertyRows);
        var propertyRoot = view.Metadata?.GetValueOrDefault("propertyRoot") as ReplayPropertyNode;
        var unknown = propertyRoot?.Children.FirstOrDefault(node => node.IsUnknown)?.SemanticValue as UnknownField;
        if (!isGeneral && _replay is { Stages.Count: > 1 })
        {
            var showChart = new MenuItem { Header = _presentation.Text("command.showStageChart"), IsEnabled = false };
            properties.ContextMenu!.Items.Add(new Separator());
            properties.ContextMenu.Items.Add(showChart);
            properties.ContextMenu.Opened += (_, _) => showChart.IsEnabled = properties.SelectedItem is PropertyUiRow row && CanShowStageChart(row);
            showChart.Click += (_, _) =>
            {
                if (properties.SelectedItem is not PropertyUiRow row || !CanShowStageChart(row)) return;
                var name = FieldName(row.Id);
                new StageComparisonDialog(_replay, _path ?? "replay.rpy", name, row.Field, _presentation, ChartNumberFormat) { Owner = this }.ShowDialog();
            };
        }
        properties.MouseDoubleClick += (_, _) =>
        {
            if (properties.SelectedItem is not PropertyUiRow selected) return;
            if (selected.Id == "Comment") EditComment();
            else if (selected.Id == "__unknown") new UnknownDataDialog(unknown, _presentation) { Owner = this }.ShowDialog();
            else if (selected.Id == "__mod") new ExtensionDataDialog(isGeneral ? _replay : null, _presentation) { Owner = this }.ShowDialog();
            else
            {
                var name = selected.Id[(selected.Id.LastIndexOf('.') + 1)..];
                var node = propertyRoot?.Children.FirstOrDefault(child => child.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (node?.SemanticValue is IEnumerable<ReplaySpellTime> spellTimes)
                    new SpellTimesDialog(spellTimes, _presentation) { Owner = this }.ShowDialog();
                else if (node?.SemanticValue is IEnumerable<ReplayCard> cards)
                    new CardsDialog(cards, _presentation.Field($"stage.{name}"), _presentation) { Owner = this }.ShowDialog();
            }
        };
        return properties;
    }

    private bool CanShowStageChart(PropertyUiRow row)
    {
        if (_replay is null || row.Id.StartsWith("__", StringComparison.Ordinal)) return false;
        return StageComparisonDialog.CanPlot(_replay, FieldName(row.Id), _presentation);
    }

    private static string FieldName(string path) => path[(path.LastIndexOf('.') + 1)..];

    private DataGrid PropertyGrid(IEnumerable<PropertyUiRow> rows)
    {
        var grid = new DataGrid { ItemsSource = rows.ToArray(), IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, Margin = new Thickness(6) };
        grid.Columns.Add(new DataGridTextColumn { Header = _presentation.Text("column.field"), Binding = new System.Windows.Data.Binding(nameof(PropertyUiRow.Field)), Width = 190, CanUserSort = false });
        grid.Columns.Add(new DataGridTextColumn { Header = _presentation.Text("column.rawField"), Binding = new System.Windows.Data.Binding(nameof(PropertyUiRow.RawField)), Width = 210, CanUserSort = false });
        grid.Columns.Add(new DataGridTextColumn { Header = _presentation.Text("column.value"), Binding = new System.Windows.Data.Binding(nameof(PropertyUiRow.Value)), Width = new DataGridLength(1, DataGridLengthUnitType.Star), CanUserSort = false });
        grid.Columns.Add(new DataGridTextColumn { Header = _presentation.Text("column.raw"), Binding = new System.Windows.Data.Binding(nameof(PropertyUiRow.Raw)), Width = new DataGridLength(1, DataGridLengthUnitType.Star), CanUserSort = false });
        DataGridFeatures.EnableCopy(grid, _presentation);
        return grid;
    }

    private System.Text.Encoding CurrentEncoding => EncodingAt(EncodingCombo.SelectedIndex);

    private static System.Text.Encoding EncodingAt(int index) => index switch
    {
        1 => ReplayTextEncoding.Utf8,
        2 => System.Text.Encoding.Default,
        _ => ReplayTextEncoding.ShiftJis
    };

    private void EditComment()
    {
        if (_replay is null || _path is null) return;
        var editor = new CommentDialog(DecodeComment(), EncodingCombo.SelectedIndex, DecodeComment, _presentation) { Owner = this };
        if (editor.ShowDialog() != true) return;
        EncodingCombo.SelectedIndex = editor.EncodingIndex;
        var target = new SaveFileDialog { Filter = "Touhou replay (*.rpy)|*.rpy", FileName = System.IO.Path.GetFileName(_path), InitialDirectory = System.IO.Path.GetDirectoryName(_path) };
        if (target.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllBytes(target.FileName, _replay.WithComment(editor.Comment, EncodingAt(editor.EncodingIndex)));
            StatusText.Text = $"{_presentation.Text("status.saved")}: {target.FileName}";
            OpenReplay(target.FileName);
        }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "RepViewer", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private string DecodeComment()
        => DecodeComment(EncodingCombo.SelectedIndex);

    private string DecodeComment(int encodingIndex)
    {
        try { return _replay?.UserData?.DecodeComment(EncodingAt(encodingIndex)) ?? ""; }
        catch { return _presentation.Text("value.decodeFailed"); }
    }

    private void EncodingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_replay is null || ReplayTabs.Items.Count == 0) return;
        if (ReplayTabs.Items[0] is TabItem general) general.Content = null;
        EnsureSelectedScope();
    }

    private void ExportClick(object sender, RoutedEventArgs e)
    {
        if (_replay is null) return;
        var dialog = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = $"{System.IO.Path.GetFileNameWithoutExtension(_path)}.csv" };
        if (dialog.ShowDialog(this) != true) return;
        var lines = new List<string> { "scope,field,value,raw" };
        lines.AddRange(_presentation.General(_replay).Select(field => Csv("general", field)));
        for (var index = 0; index < _replay.Stages.Count; index++) lines.AddRange(_presentation.Stage(_replay, index).Select(field => Csv($"stage.{_replay.Stages[index].StageId}", field)));
        File.WriteAllLines(dialog.FileName, lines, new UTF8Encoding(true));
        StatusText.Text = $"{_presentation.Text("status.exported")}: {dialog.FileName}";
    }

    private static string Csv(string scope, DisplayField field) => string.Join(',', new[] { scope, field.Label, field.Text, DisplayFormatter.FormatRaw(field.RawValue) }.Select(value => $"\"{value.Replace("\"", "\"\"")}\""));

    private void SettingsClick(object sender, RoutedEventArgs e)
    {
        var associated = false;
        try { associated = FileAssociationService.GetStatus() is { IsAssociated: true, MatchesCurrentPath: true }; }
        catch { }
        var dialog = new SettingsDialog(_locale, _presentation, _plugins.Plugins.Select(plugin => plugin.Id), _enabledPlugins, associated,
            _chartUseScientificNotation, _chartUseThousandsSeparator, _uiScalePercent) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try
        {
            if (dialog.FileAssociated != associated)
            {
                if (dialog.FileAssociated) FileAssociationService.AssociateCurrent(refreshExplorer: true);
                else FileAssociationService.Unassociate(suppressPrompt: true, refreshExplorer: true);
            }
        }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "RepViewer", MessageBoxButton.OK, MessageBoxImage.Warning); }
        _locale = dialog.Locale;
        _enabledPlugins = dialog.EnabledPluginIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _chartUseScientificNotation = dialog.ChartUseScientificNotation;
        _chartUseThousandsSeparator = dialog.ChartUseThousandsSeparator;
        _uiScalePercent = dialog.UiScalePercent;
        UiScaleService.SetScale(_uiScalePercent / 100d);
        try
        {
            AppSettingsStore.Save(new AppSettings
            {
                Locale = _locale,
                ChartUseScientificNotation = _chartUseScientificNotation,
                ChartUseThousandsSeparator = _chartUseThousandsSeparator,
                UiScalePercent = _uiScalePercent,
                EnabledPluginIds = _enabledPlugins.Order(StringComparer.OrdinalIgnoreCase).ToArray()
            });
        }
        catch (Exception exception) { MessageBox.Show(this, $"{_presentation.Text("settings.saveFailed")}: {exception.Message}", "RepViewer", MessageBoxButton.OK, MessageBoxImage.Warning); }
        _presentation = PresentationCatalog.Load(PresentationCatalog.DefaultRoot, _locale, _replay?.Identity.GameId ?? "th06");
        ApplyLanguage();
        if (_replay is not null) RebuildTabsPreservingSelection();
    }

    public void HandleAssociationStartup()
    {
        try
        {
            if (FileAssociationService.PromptSuppressed)
            {
                FileAssociationService.Unassociate(suppressPrompt: true, refreshExplorer: false, notifyShell: false);
                return;
            }
            var status = FileAssociationService.GetStatus();
            if (status.IsAssociated && status.MatchesCurrentPath)
            {
                FileAssociationService.EnsureCurrentRegistration();
                return;
            }
            var prompt = new AssociationPromptDialog { Owner = this };
            prompt.ShowDialog();
            if (prompt.Result == AssociationPromptResult.Yes) FileAssociationService.AssociateCurrent(refreshExplorer: true);
            else if (prompt.Result == AssociationPromptResult.Never) FileAssociationService.Unassociate(suppressPrompt: true, refreshExplorer: false, notifyShell: false);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"文件关联设置失败：{exception.Message}", "RepViewer", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void EditPunishment()
    {
        var dialog = new PunishmentDialog(_punishment, _presentation) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        dialog.ApplyTo(_punishment);
        if (_replay is not null) RebuildTabsPreservingSelection();
    }

    private void RebuildTabsPreservingSelection()
    {
        var outerIndex = ReplayTabs.SelectedIndex;
        var innerId = (ReplayTabs.SelectedItem as TabItem)?.Content is TabControl inner
            ? (inner.SelectedItem as TabItem)?.Tag as string
            : null;
        PopulateReplayTabs();
        if (ReplayTabs.Items.Count == 0) return;
        ReplayTabs.SelectedIndex = Math.Clamp(outerIndex, 0, ReplayTabs.Items.Count - 1);
        EnsureSelectedScope();
        if (innerId is null || (ReplayTabs.SelectedItem as TabItem)?.Content is not TabControl rebuilt) return;
        var selected = rebuilt.Items.OfType<TabItem>().FirstOrDefault(item => string.Equals(item.Tag as string, innerId, StringComparison.Ordinal));
        if (selected is not null) rebuilt.SelectedItem = selected;
    }

    private void ApplyLanguage()
    {
        Title = $"{_presentation.Text("app.title")} {ApplicationVersion}"; OpenButton.Content = _presentation.Text("command.open");
        ExportButton.Content = _presentation.Text("command.export"); SettingsButton.Content = _presentation.Text("command.settings");
        EncodingLabel.Text = _presentation.Text("settings.commentEncoding");
    }

    private ChartNumberFormatOptions ChartNumberFormat => new(_chartUseScientificNotation, _chartUseThousandsSeparator);
    private static string DefaultLocale() => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase) ? "zh-CN" : "en-US";

    private sealed record PropertyUiRow(string Id, string Field, string RawField, string Value, string Raw);
}
