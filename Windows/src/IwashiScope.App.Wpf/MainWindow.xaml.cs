using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using IwashiScope.App.Wpf.ColorManagement;
using IwashiScope.App.Wpf.Export;
using IwashiScope.App.Wpf.Layout;
using IwashiScope.App.Wpf.Updates;
using IwashiScope.App.Wpf.ViewModels;
using IwashiScope.Core.Calculations;
using IwashiScope.Core.Models;
using IwashiScope.Core.History;
using IwashiScope.Core.Export;
using Microsoft.Win32;

namespace IwashiScope.App.Wpf;

public partial class MainWindow : Window
{
    private const string InternalHistoryFormat = "IwashiScope.HistoryEntryIds";
    private readonly MainWindowViewModel _viewModel = new();
    private readonly MeasurementExportService _exportService = new();
    private readonly DragExportCache _dragExportCache = new();
    private readonly HistoryDragCoordinator _historyDragCoordinator = new();
    private readonly WinSparkleUpdater _updater;
    private readonly DisplayProfileController _displayProfiles;
    private Point _dragStart;
    private bool _isApplyingSelection;
    private bool _shutdownApproved;
    private readonly HashSet<(MeasurementMode Mode, string Date)> _collapsedHistoryDates = [];
    private HistoryDragContext? _activeHistoryDrag;

    private sealed class HistoryDragContext(MeasurementMode mode, IReadOnlySet<Guid> ids)
    {
        public string Token { get; } = Guid.NewGuid().ToString("N");
        public MeasurementMode Mode { get; } = mode;
        public IReadOnlySet<Guid> Ids { get; } = ids;
        public bool HasInternalDrop { get; set; }
        public Guid? DropBeforeId { get; set; }
    }

    public MainWindow()
    {
        InitializeComponent();
        _updater = ((App)Application.Current).Updater;
        DataContext = _viewModel;
        _displayProfiles = new DisplayProfileController(this, _viewModel.SetDisplayColors);
        var historyView = CollectionViewSource.GetDefaultView(_viewModel.HistoryItems);
        var dateGrouping = new PropertyGroupDescription(nameof(HistoryItemViewModel.DateKey));
        dateGrouping.SortDescriptions.Add(new SortDescription(nameof(CollectionViewGroup.Name), ListSortDirection.Descending));
        historyView.GroupDescriptions.Add(dateGrouping);
        SpotreadLogTextBox.Text = _viewModel.LogText;
        _viewModel.LogAppended += AppendLog;
        _viewModel.LogReset += ResetLog;
        _viewModel.HistoryRefreshed += ApplyHistorySelection;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        Loaded += MainWindow_Loaded;
        PreviewMouseLeftButtonUp += (_, _) =>
        {
            if (!_historyDragCoordinator.IsDragging) _historyDragCoordinator.Cancel();
        };
        Deactivated += (_, _) =>
        {
            if (!_historyDragCoordinator.IsDragging) _historyDragCoordinator.Cancel();
        };
        HistoryList.QueryContinueDrag += (_, e) =>
        {
            if (_historyDragCoordinator.IsCancellationRequested)
            {
                e.Action = DragAction.Cancel;
                e.Handled = true;
            }
        };
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
        ApplyHistorySelection();
        _updater.TryInitialize(
            _viewModel.Language,
            CanCloseForUpdate,
            RequestCloseForUpdate);
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.Mode))
        {
            _historyDragCoordinator.Cancel();
            ResetExportOptions();
        }
        if (e.PropertyName == nameof(MainWindowViewModel.IsModeSelectionVisible) &&
            _viewModel.IsModeSelectionVisible) _historyDragCoordinator.Cancel();
    }

    private void AppendLog(string text)
    {
        SpotreadLogTextBox.AppendText(text);
        if (FollowLatestCheckBox.IsChecked == true)
        {
            SpotreadLogTextBox.ScrollToEnd();
        }
    }

    private void ResetLog(string text)
    {
        SpotreadLogTextBox.Text = text;
        if (FollowLatestCheckBox.IsChecked == true)
        {
            SpotreadLogTextBox.ScrollToEnd();
        }
    }

    private async void OpenWorkspace_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscardUnsaved())
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = _viewModel.OpenWorkspaceLabel,
            Filter = "IwashiScope Workspace (*.iwashiscope)|*.iwashiscope|JSON (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            await _viewModel.RestoreWorkspaceAsync(dialog.FileName);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "IwashiScope",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void SaveWorkspace_Click(object sender, RoutedEventArgs e)
    {
        await SaveWorkspaceWithDialogAsync();
    }

    private async Task<bool> SaveWorkspaceWithDialogAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = _viewModel.SaveWorkspaceLabel,
            Filter = "IwashiScope Workspace (*.iwashiscope)|*.iwashiscope",
            DefaultExt = ".iwashiscope",
            AddExtension = true,
            FileName = $"IwashiScope {DateTime.Now:yyyy-MM-dd HHmm}.iwashiscope",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return false;
        }

        try
        {
            await _viewModel.SaveWorkspaceAsync(dialog.FileName);
            return true;
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "IwashiScope",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var entries = _viewModel.SelectedEntries();
        if (entries.Count == 0)
        {
            MessageBox.Show(
                this,
                "書き出す測定履歴を選択してください。\nSelect one or more measurements to export.",
                "IwashiScope",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = _viewModel.ExportLabel,
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var paths = await _exportService.ExportAsync(
                dialog.FolderName,
                entries,
                CurrentExportOptions(),
                _viewModel.OrderedEntries());
            MessageBox.Show(
                this,
                $"{paths.Count} files exported.\n{dialog.FolderName}",
                "IwashiScope",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "IwashiScope",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void DeleteAllHistory_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.HasDeletableHistory)
        {
            return;
        }
        if (MessageBox.Show(
                this,
                _viewModel.DeleteHistoryConfirmationMessage,
                _viewModel.DeleteHistoryConfirmationTitle,
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning) == MessageBoxResult.OK)
        {
            _viewModel.DeleteAllHistory();
        }
    }

    private sealed record HistoryDeletionRequest(MeasurementMode Mode, IReadOnlySet<Guid> Ids, string Title);
    private sealed record HistoryDateExportRequest(
        MeasurementMode Mode, MeasurementHistoryDateGroup Group,
        IReadOnlyList<MeasurementHistoryEntry> OrderedEntries, bool PracticalRange, SpectrumYAxisConfiguration YAxis);
    private sealed record SwatchExportRequest(byte[] Data, string SuggestedName);

    private void HistoryDateExpander_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is Expander expander && expander.DataContext is CollectionViewGroup group)
            expander.IsExpanded = !_collapsedHistoryDates.Contains((_viewModel.Mode, group.Name.ToString() ?? string.Empty));
    }

    private void HistoryDateExpander_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not Expander expander || !expander.IsLoaded ||
            expander.DataContext is not CollectionViewGroup group || !ReferenceEquals(e.Source, expander)) return;
        var key = (_viewModel.Mode, group.Name.ToString() ?? string.Empty);
        if (expander.IsExpanded) _collapsedHistoryDates.Remove(key);
        else _collapsedHistoryDates.Add(key);
    }

    private void HistoryCard_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: HistoryItemViewModel item, ContextMenu: { } menu } ||
            !item.IsRegularHistoryItem) return;
        var ids = _viewModel.ContextMenuEntryIds(item.Id);
        var ordered = _viewModel.OrderedEntries();
        var entries = ordered.Where(entry => ids.Contains(entry.Id)).ToArray();
        var deletion = _viewModel.DeletableEntryIds(ids, _viewModel.Mode);
        var japanese = _viewModel.Language == "ja";
        foreach (var action in menu.Items.OfType<MenuItem>())
        {
            // Items remain at their declared positions; Tags hold immutable request snapshots.
            if (ReferenceEquals(action, menu.Items[menu.Items.Count - 1]))
            {
                action.Header = japanese ? "選択履歴を削除" : "Delete Selected History";
                action.IsEnabled = deletion.Count > 0;
                action.Tag = new HistoryDeletionRequest(_viewModel.Mode, deletion,
                    japanese ? "選択履歴を削除しますか？" : "Delete the selected history?");
            }
            else if (ReferenceEquals(action, menu.Items[1]))
            {
                action.Header = japanese ? "選択カードをスウォッチに書き出し" : "Export Selected Cards as Swatches";
                action.IsEnabled = item.IsReflectance && entries.Length > 0 && entries.All(entry => entry.Measurement.Lab is not null);
                action.Tag = null;
                if (action.IsEnabled)
                {
                    var names = MeasurementExportFileNamer.BaseNames(entries, ordered);
                    try
                    {
                        action.Tag = new SwatchExportRequest(
                            AdobeSwatchExchangeEncoder.Encode(entries.Select(entry =>
                                new AdobeLabSwatch(names[entry.Id], entry.Measurement.Lab!)).ToArray()),
                            Path.GetFileNameWithoutExtension(MeasurementExportFileNamer.CombinedSwatchFileName(entries, ordered)));
                    }
                    catch (InvalidDataException) { action.IsEnabled = false; }
                }
            }
        }
    }

    private void HistoryDate_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CollectionViewGroup collection, ContextMenu: { } menu }) return;
        var entries = collection.Items.OfType<HistoryItemViewModel>()
            .Where(item => item.IsRegularHistoryItem).Select(item => item.Entry).ToArray();
        if (entries.Length == 0) { e.Handled = true; return; }
        var group = new MeasurementHistoryDateGroup(MeasurementHistoryDateGrouping.LocalDate(entries[0]), entries);
        var ids = _viewModel.DeletableEntryIds(entries.Select(entry => entry.Id), _viewModel.Mode);
        var japanese = _viewModel.Language == "ja";
        var export = (MenuItem)menu.Items[0];
        export.Header = japanese ? $"{group.ExportName}の履歴を書きだし" : $"Export History from {group.ExportName}";
        export.IsEnabled = MeasurementExportService.CanExportDate(group, _viewModel.Mode);
        export.Tag = new HistoryDateExportRequest(_viewModel.Mode, group, _viewModel.OrderedEntries(),
            _viewModel.UsePracticalRange, _viewModel.YAxisConfiguration);
        var delete = (MenuItem)menu.Items[2];
        delete.Header = japanese ? $"{group.Title}の履歴を削除" : $"Delete History from {group.Title}";
        delete.IsEnabled = ids.Count > 0;
        delete.Tag = new HistoryDeletionRequest(_viewModel.Mode, ids,
            japanese ? $"{group.Title}の履歴を削除しますか？" : $"Delete the history from {group.Title}?");
    }

    private void HistoryContextDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: HistoryDeletionRequest request } || request.Ids.Count == 0) return;
        var message = _viewModel.Language == "ja"
            ? $"対象の{request.Ids.Count}件の測定履歴を削除します。ユーザー定義光源に登録中の履歴は残ります。この操作は取り消せません。"
            : $"Delete these {request.Ids.Count} records. User illuminant registrations will be kept. This cannot be undone.";
        if (MessageBox.Show(this, message, request.Title, MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK)
            _viewModel.DeleteEntries(request.Ids, request.Mode);
    }

    private async void HistorySwatchExport_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: SwatchExportRequest request })
            await SaveSwatchesWithDialogAsync(request.Data, request.SuggestedName);
    }

    private async Task SaveSwatchesWithDialogAsync(byte[] data, string suggestedName)
    {
        var dialog = new SaveFileDialog
        {
            Title = _viewModel.Language == "ja" ? "Lab特色スウォッチを書き出し" : "Export Lab Spot-Color Swatches",
            Filter = "Adobe Swatch Exchange (*.ase)|*.ase", DefaultExt = ".ase", AddExtension = true,
            FileName = suggestedName,
        };
        if (dialog.ShowDialog(this) != true) return;
        try { await IwashiScope.Infrastructure.Windows.Storage.AtomicFile.WriteAllBytesAsync(dialog.FileName, data); }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "IwashiScope", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void HistoryDateExport_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: HistoryDateExportRequest request }) return;
        try
        {
            if (request.Mode == MeasurementMode.Reflectance)
            {
                await SaveSwatchesWithDialogAsync(
                    MeasurementExportService.DateSwatches(request.Group, request.OrderedEntries), request.Group.ExportName);
                return;
            }
            var dialog = new OpenFolderDialog
            {
                Title = _viewModel.Language == "ja" ? $"{request.Group.ExportName}の履歴を書きだし" : $"Export History from {request.Group.ExportName}",
                Multiselect = false,
            };
            if (dialog.ShowDialog(this) != true) return;
            await _exportService.ExportLightingDateAsync(dialog.FolderName, request.Group, request.OrderedEntries,
                request.Mode, request.PracticalRange, request.YAxis);
        }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "IwashiScope", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void Japanese_Click(object sender, RoutedEventArgs e) => _viewModel.Language = "ja";
    private void English_Click(object sender, RoutedEventArgs e) => _viewModel.Language = "en";

    private void Licenses_Click(object sender, RoutedEventArgs e)
    {
        var license = Path.Combine(AppContext.BaseDirectory, "LICENSE");
        var notice = Path.Combine(AppContext.BaseDirectory, "THIRD_PARTY_NOTICES.md");
        var source = "https://github.com/Yamonov/IwashiScope";
        var version = Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "1.0.4";
        var result = MessageBox.Show(
            this,
            $"IwashiScope {version}: AGPL-3.0-only\n" +
            "iwashiscope-spotread and bundled components retain their original licenses.\n\n" +
            $"Source: {source}\n" +
            $"License: {license}\n" +
            $"Notices: {notice}\n\n" +
            "配布フォルダーをExplorerで開きますか？",
            "Licenses & Source Code",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);
        if (result == MessageBoxResult.Yes)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                ArgumentList = { AppContext.BaseDirectory },
                UseShellExecute = false,
            });
        }
    }

    private void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _updater.CheckForUpdates();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                _viewModel.Language == "ja"
                    ? $"アップデートを確認できませんでした。\n{exception.Message}"
                    : $"Unable to check for updates.\n{exception.Message}",
                "IwashiScope",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private bool CanCloseForUpdate()
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return false;
        }
        return Dispatcher.Invoke(() =>
            !_historyDragCoordinator.IsBusy && UpdateShutdownPolicy.CanShutdown(
                _viewModel.HasUnsavedChanges,
                _viewModel.IsBusy));
    }

    private void RequestCloseForUpdate()
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return;
        }
        Dispatcher.BeginInvoke(CloseForUpdateAsync);
    }

    private async void CloseForUpdateAsync()
    {
        if (!CanCloseForUpdate())
        {
            return;
        }
        _shutdownApproved = true;
        _dragExportCache.Dispose();
        await _viewModel.DisposeAsync();
        Close();
    }

    private void HistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingSelection || _viewModel.IsRefreshingHistory)
        {
            return;
        }

        var selected = HistoryList.SelectedItems
            .OfType<HistoryItemViewModel>()
            .Where(item => item.CanSelect)
            .Select(item => item.Id)
            .ToArray();
        var selectedItem = HistoryList.SelectedItem as HistoryItemViewModel;
        Guid? active = selectedItem?.CanSelect == true ? selectedItem.Id : null;
        _viewModel.SynchronizeSelection(selected, active);
    }

    private void ApplyHistorySelection()
    {
        var dateKeys = _viewModel.HistoryItems.Select(item => item.DateKey).ToHashSet();
        _collapsedHistoryDates.RemoveWhere(key => key.Mode == _viewModel.Mode && !dateKeys.Contains(key.Date));
        _isApplyingSelection = true;
        try
        {
            HistoryList.SelectedItems.Clear();
            foreach (var item in _viewModel.HistoryItems.Where(item => item.IsSelected))
            {
                HistoryList.SelectedItems.Add(item);
            }
        }
        finally
        {
            _isApplyingSelection = false;
        }
    }

    private void HistoryList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(HistoryList);
        var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item?.DataContext is HistoryItemViewModel { CanSelect: true } &&
            FindAncestor<TextBox>(e.OriginalSource as DependencyObject) is null)
            _historyDragCoordinator.Arm();
        else
            _historyDragCoordinator.Cancel();
    }

    private async void HistoryList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_historyDragCoordinator.IsBusy || e.LeftButton != MouseButtonState.Pressed ||
            FindAncestor<TextBox>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        var current = e.GetPosition(HistoryList);
        if (Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var entries = _viewModel.SelectedEntries();
        if (entries.Count == 0)
        {
            return;
        }

        try
        {
            var context = new HistoryDragContext(_viewModel.Mode, entries.Select(entry => entry.Id).ToHashSet());
            var dragOptions = MeasurementExportOptions.ForDrag(
                context.Mode, _viewModel.UsePracticalRange, _viewModel.YAxisConfiguration);
            var ordered = _viewModel.OrderedEntries();
            await _historyDragCoordinator.TryRunAsync(
                cancellation =>
                {
                    Cursor = Cursors.Wait;
                    return _dragExportCache.CreateAsync(entries, dragOptions, ordered, cancellation);
                },
                () => !_shutdownApproved && IsActive && _viewModel.IsWorkspaceVisible &&
                    Mouse.LeftButton == MouseButtonState.Pressed && _viewModel.Mode == context.Mode &&
                    context.Ids.SetEquals(_viewModel.SelectedEntries().Select(entry => entry.Id)),
                paths =>
                {
                    if (paths.Count == 0) return;
                    var data = new DataObject();
                    // A per-operation string token avoids exporting serialized application objects.
                    data.SetData(InternalHistoryFormat, context.Token, autoConvert: false);
                    data.SetData(DataFormats.FileDrop, paths.ToArray(), autoConvert: false);
                    Cursor = Cursors.Arrow;
                    _activeHistoryDrag = context;
                    try
                    {
                        var effect = DragDrop.DoDragDrop(HistoryList, data, DragDropEffects.Move | DragDropEffects.Copy);
                        // Do not rebuild WPF drop targets inside the OLE Drop callback.
                        if (effect == DragDropEffects.Move && context.HasInternalDrop &&
                            !_historyDragCoordinator.IsCancellationRequested)
                            _viewModel.ReorderEntriesBefore(context.Mode, context.Ids, context.DropBeforeId);
                    }
                    finally { _activeHistoryDrag = null; }
                });
        }
        catch (Exception exception)
        {
            if (!_shutdownApproved)
                MessageBox.Show(this, exception.Message, "IwashiScope", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { Cursor = Cursors.Arrow; }
    }

    private bool IsCurrentHistoryDrag(DragEventArgs e) =>
        _historyDragCoordinator.IsDragging && !_historyDragCoordinator.IsCancellationRequested &&
        _activeHistoryDrag is { } context && context.Mode == _viewModel.Mode &&
        e.AllowedEffects.HasFlag(DragDropEffects.Move) &&
        e.Data.GetDataPresent(InternalHistoryFormat, autoConvert: false) &&
        e.Data.GetData(InternalHistoryFormat, autoConvert: false) is string token && token == context.Token;

    private void HistoryList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = IsCurrentHistoryDrag(e)
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void HistoryList_Drop(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        e.Handled = true;
        if (!IsCurrentHistoryDrag(e))
        {
            return;
        }

        var target = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        var targetItem = target?.DataContext as HistoryItemViewModel;
        _activeHistoryDrag!.HasInternalDrop = true;
        _activeHistoryDrag.DropBeforeId = targetItem?.CanSelect == true ? targetItem.Id : null;
        e.Effects = DragDropEffects.Move;
    }

    private void HistoryNameEditor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.ImeProcessed ||
            e.Key is not (Key.Enter or Key.Return or Key.Tab) ||
            sender is not TextBox editor)
        {
            return;
        }

        editor.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        if (e.Key is Key.Enter or Key.Return)
        {
            editor.IsReadOnly = true;
            HistoryList.Focus();
            e.Handled = true;
            return;
        }

        var item = FindAncestor<ListBoxItem>(editor);
        if (item is null)
        {
            return;
        }
        var index = HistoryList.ItemContainerGenerator.IndexFromContainer(item);
        if (index < 0 || HistoryList.Items.Count == 0)
        {
            return;
        }

        var backwards = e.Key == Key.Tab &&
                        Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var targetIndex = backwards
            ? (index - 1 + HistoryList.Items.Count) % HistoryList.Items.Count
            : (index + 1) % HistoryList.Items.Count;
        HistoryList.ScrollIntoView(HistoryList.Items[targetIndex]);
        if (HistoryList.ItemContainerGenerator.ContainerFromIndex(targetIndex)
                is ListBoxItem target &&
            FindDescendant<TextBox>(target) is { } targetEditor)
        {
            editor.IsReadOnly = true;
            targetEditor.IsReadOnly = false;
            targetEditor.Focus();
            targetEditor.CaretIndex = targetEditor.Text.Length;
            e.Handled = true;
        }
    }

    private void HistoryNameEditor_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBox editor ||
            editor.DataContext is HistoryItemViewModel { CanRename: false })
        {
            return;
        }
        editor.IsReadOnly = false;
        editor.Focus();
        editor.CaretIndex = editor.Text.Length;
        e.Handled = true;
    }

    private void HistoryRenameMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: HistoryItemViewModel item } ||
            !item.CanRename ||
            HistoryList.ItemContainerGenerator.ContainerFromItem(item) is not ListBoxItem container ||
            FindVisibleDescendant<TextBox>(container) is not { } editor)
        {
            return;
        }
        HistoryList.ScrollIntoView(item);
        editor.IsReadOnly = false;
        editor.Focus();
        editor.CaretIndex = editor.Text.Length;
    }

    private void RegisterUserIlluminant_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem
            {
                DataContext: HistoryItemViewModel item,
                Tag: string slotName,
            } &&
            Enum.TryParse<UserIlluminantSlot>(slotName, out var slot))
        {
            _viewModel.RegisterUserIlluminant(item.Id, slot);
        }
    }

    private void RemoveUserIlluminant_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: HistoryItemViewModel item })
        {
            _viewModel.RemoveUserIlluminantRegistrations(item.Id);
        }
    }

    private void HistoryNameEditor_LostKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox editor)
        {
            editor.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            editor.IsReadOnly = true;
        }
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_shutdownApproved)
        {
            return;
        }

        if (_historyDragCoordinator.IsBusy)
        {
            e.Cancel = true;
            _historyDragCoordinator.Cancel();
            await _historyDragCoordinator.WhenIdle;
            Close();
            return;
        }

        if (_viewModel.HasUnsavedChanges)
        {
            var result = MessageBox.Show(
                this,
                "保存していない測定結果があります。終了前に保存しますか？\n" +
                "There are unsaved measurements. Save before closing?",
                "IwashiScope",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);
            if (result == MessageBoxResult.Cancel)
            {
                e.Cancel = true;
                return;
            }
            if (result == MessageBoxResult.Yes)
            {
                e.Cancel = true;
                if (!await SaveWorkspaceWithDialogAsync())
                {
                    return;
                }
            }
        }

        e.Cancel = true;
        _shutdownApproved = true;
        _dragExportCache.Dispose();
        await _viewModel.DisposeAsync();
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_historyDragCoordinator.IsBusy)
            {
                _historyDragCoordinator.Cancel();
                e.Handled = true;
                return;
            }
            if (_viewModel.IsWorkspaceVisible)
            {
                _viewModel.ReturnToModeSelectionCommand.Execute(null);
            }
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.S:
                    SaveWorkspace_Click(sender, e);
                    e.Handled = true;
                    return;
                case Key.O:
                    OpenWorkspace_Click(sender, e);
                    e.Handled = true;
                    return;
                case Key.E:
                    Export_Click(sender, e);
                    e.Handled = true;
                    return;
                case Key.A when HistoryList.IsKeyboardFocusWithin:
                    _viewModel.SelectAll();
                    e.Handled = true;
                    return;
                case Key.D when HistoryList.IsKeyboardFocusWithin:
                    _viewModel.DeselectAll();
                    e.Handled = true;
                    return;
            }
        }

        if (e.Key == Key.Delete && HistoryList.IsKeyboardFocusWithin)
        {
            _viewModel.DeleteSelection();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Alt &&
                 e.Key is Key.Up or Key.Down &&
                 HistoryList.IsKeyboardFocusWithin)
        {
            if (e.Key == Key.Up)
            {
                _viewModel.MoveUpCommand.Execute(null);
            }
            else
            {
                _viewModel.MoveDownCommand.Execute(null);
            }
            e.Handled = true;
        }
    }

    private bool ConfirmDiscardUnsaved()
    {
        if (!_viewModel.HasUnsavedChanges)
        {
            return true;
        }
        return MessageBox.Show(
                   this,
                   "保存していない変更は、ワークスペースを復帰すると失われます。\n" +
                   "Unsaved changes will be lost when restoring a workspace.",
                   "IwashiScope",
                   MessageBoxButton.OKCancel,
                   MessageBoxImage.Warning) == MessageBoxResult.OK;
    }

    private MeasurementExportOptions CurrentExportOptions() =>
        _viewModel.Mode == MeasurementMode.Reflectance
            ? new MeasurementExportOptions
            {
                SpectrumPng = ReflectanceSpectrumPngCheckBox.IsChecked == true,
                CriPng = false,
                Tm30Png = false,
                Csv = ReflectanceCsvCheckBox.IsChecked == true,
                Ase = ExportAseCheckBox.IsChecked == true,
                UsePracticalSpectrumRange = _viewModel.UsePracticalRange,
                SpectrumYAxisConfiguration = _viewModel.YAxisConfiguration,
            }
            : new MeasurementExportOptions
            {
                SpectrumPng = LightingSpectrumPngCheckBox.IsChecked == true,
                CriPng = ExportCriPngCheckBox.IsChecked == true,
                Tm30Png = ExportTm30PngCheckBox.IsChecked == true,
                Csv = LightingCsvCheckBox.IsChecked == true,
                Ase = false,
                UsePracticalSpectrumRange = _viewModel.UsePracticalRange,
                ShowD50 = ExportD50CheckBox.IsChecked == true,
                ShowD65 = ExportD65CheckBox.IsChecked == true,
                ShowLms = ExportLmsCheckBox.IsChecked == true,
                SpectrumYAxisConfiguration = _viewModel.YAxisConfiguration,
            };

    private void ResetExportOptions()
    {
        ExportAseCheckBox.IsChecked = true;
        ReflectanceSpectrumPngCheckBox.IsChecked = false;
        ReflectanceCsvCheckBox.IsChecked = false;
        LightingSpectrumPngCheckBox.IsChecked = true;
        ExportD50CheckBox.IsChecked = false;
        ExportD65CheckBox.IsChecked = false;
        ExportLmsCheckBox.IsChecked = false;
        ExportCriPngCheckBox.IsChecked = false;
        ExportTm30PngCheckBox.IsChecked = false;
        LightingCsvCheckBox.IsChecked = false;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T found)
            {
                return found;
            }
            current = current is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : current is FrameworkContentElement content ? content.Parent : LogicalTreeHelper.GetParent(current);
        }
        return null;
    }

    private static T? FindDescendant<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T found)
            {
                return found;
            }
            if (FindDescendant<T>(child) is { } nested)
            {
                return nested;
            }
        }
        return null;
    }

    private static T? FindVisibleDescendant<T>(DependencyObject parent)
        where T : FrameworkElement
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T { IsVisible: true } found)
            {
                return found;
            }
            if (FindVisibleDescendant<T>(child) is { } nested)
            {
                return nested;
            }
        }
        return null;
    }
}
