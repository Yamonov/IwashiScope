using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using IwashiScope.App.Wpf.Export;
using IwashiScope.App.Wpf.Layout;
using IwashiScope.App.Wpf.Updates;
using IwashiScope.App.Wpf.ViewModels;
using IwashiScope.Core.Models;
using Microsoft.Win32;

namespace IwashiScope.App.Wpf;

public partial class MainWindow : Window
{
    private const string InternalHistoryFormat = "IwashiScope.HistoryEntryIds";
    private readonly MainWindowViewModel _viewModel = new();
    private readonly MeasurementExportService _exportService = new();
    private readonly DragExportCache _dragExportCache = new();
    private readonly WinSparkleUpdater _updater;
    private Point _dragStart;
    private bool _isApplyingSelection;
    private bool _historyExpanded;
    private double _collapsedHistoryHeight = 220;
    private bool _shutdownApproved;

    public MainWindow()
    {
        InitializeComponent();
        _updater = ((App)Application.Current).Updater;
        DataContext = _viewModel;
        SpotreadLogTextBox.Text = _viewModel.LogText;
        _viewModel.LogAppended += AppendLog;
        _viewModel.LogReset += ResetLog;
        _viewModel.HistoryRefreshed += ApplyHistorySelection;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
        ResetAdaptiveHistoryHeight();
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
            ResetAdaptiveHistoryHeight();
            ResetExportOptions();
        }
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

    private void Japanese_Click(object sender, RoutedEventArgs e) => _viewModel.Language = "ja";
    private void English_Click(object sender, RoutedEventArgs e) => _viewModel.Language = "en";

    private void Licenses_Click(object sender, RoutedEventArgs e)
    {
        var license = Path.Combine(AppContext.BaseDirectory, "LICENSE");
        var notice = Path.Combine(AppContext.BaseDirectory, "THIRD_PARTY_NOTICES.md");
        var source = "https://github.com/Yamonov/IwashiScope";
        var version = Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "0.9.6";
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
            UpdateShutdownPolicy.CanShutdown(
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

    private void HistoryList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        _dragStart = e.GetPosition(HistoryList);

    private async void HistoryList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed ||
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
            Cursor = Cursors.Wait;
            var dragOptions = _viewModel.Mode == MeasurementMode.Reflectance
                ? CurrentExportOptions() with
                {
                    SpectrumPng = false,
                    CriPng = false,
                    Tm30Png = false,
                    Csv = false,
                    Ase = true,
                    SpectrumYAxisConfiguration = _viewModel.YAxisConfiguration,
                }
                : CurrentExportOptions() with
                {
                    SpectrumPng = true,
                    CriPng = true,
                    Tm30Png = true,
                    Csv = false,
                    Ase = false,
                    ShowD50 = false,
                    ShowD65 = false,
                };
            var paths = await _dragExportCache.CreateAsync(
                entries,
                dragOptions,
                _viewModel.OrderedEntries());
            var data = new DataObject();
            data.SetData(InternalHistoryFormat, entries.Select(entry => entry.Id).ToArray());
            data.SetData(DataFormats.FileDrop, paths.ToArray());
            Cursor = Cursors.Arrow;
            DragDrop.DoDragDrop(HistoryList, data, DragDropEffects.Move | DragDropEffects.Copy);
        }
        catch (Exception exception)
        {
            Cursor = Cursors.Arrow;
            MessageBox.Show(this, exception.Message, "IwashiScope", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void HistoryList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(InternalHistoryFormat)
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void HistoryList_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(InternalHistoryFormat))
        {
            return;
        }

        var target = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        var targetItem = target?.DataContext as HistoryItemViewModel;
        _viewModel.ReorderSelectionBefore(targetItem?.CanSelect == true ? targetItem.Id : null);
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
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

    private void HistoryToggle_Click(object sender, RoutedEventArgs e)
    {
        var maximum = AnalysisHistoryGrid.ActualHeight * HistoryFooterLayout.ExpandedFraction;
        if (_historyExpanded || HistoryRow.ActualHeight >= maximum - 1)
        {
            HistoryRow.Height = new GridLength(
                Math.Min(
                    Math.Max(HistoryFooterLayout.MinimumCollapsedHeight, _collapsedHistoryHeight),
                    AnalysisHistoryGrid.ActualHeight * HistoryFooterLayout.MaximumCollapsedFraction));
            _historyExpanded = false;
        }
        else
        {
            _collapsedHistoryHeight = HistoryRow.ActualHeight;
            HistoryRow.Height = new GridLength(maximum);
            _historyExpanded = true;
        }
    }

    private void HistorySplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        ClampHistoryHeight();
        _collapsedHistoryHeight = HistoryRow.ActualHeight;
        _historyExpanded = false;
    }

    private void AnalysisHistoryGrid_SizeChanged(object sender, SizeChangedEventArgs e) =>
        ClampHistoryHeight();

    private void ClampHistoryHeight()
    {
        var maximum = AnalysisHistoryGrid.ActualHeight * HistoryFooterLayout.ExpandedFraction;
        if (HistoryRow.ActualHeight > maximum)
        {
            HistoryRow.Height = new GridLength(maximum);
        }
    }

    private void ResetAdaptiveHistoryHeight()
    {
        if (AnalysisHistoryGrid.ActualHeight <= 0)
        {
            return;
        }
        _collapsedHistoryHeight = HistoryFooterLayout.CollapsedHeight(
            AnalysisHistoryGrid.ActualHeight,
            analysisContentHeight: 0,
            _viewModel.Mode);
        HistoryRow.Height = new GridLength(_collapsedHistoryHeight);
        _historyExpanded = false;
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_shutdownApproved)
        {
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
            current = VisualTreeHelper.GetParent(current);
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
}
