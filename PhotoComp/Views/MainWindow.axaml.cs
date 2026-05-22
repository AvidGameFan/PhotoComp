using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using PhotoComp.ViewModels;

namespace PhotoComp.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _lastVm;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_lastVm is not null)
            _lastVm.PropertyChanged -= OnVmPropertyChanged;

        if (DataContext is not MainWindowViewModel vm) return;

        vm.PickSourceFolderAsync = PickFolderAsync;
        vm.PickDestFolderAsync   = PickFolderAsync;
        vm.ShowAlertAsync        = ShowAlertAsync;
        vm.ConfirmAsync          = ConfirmAsync;
        vm.PropertyChanged += OnVmPropertyChanged;
        _lastVm = vm;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MainWindowViewModel vm) return;

        if (e.PropertyName == nameof(MainWindowViewModel.IsLoading))
        {
            Cursor = vm.IsLoading
                ? new Cursor(StandardCursorType.Wait)
                : Cursor.Default;
            return;
        }

        if (e.PropertyName is nameof(MainWindowViewModel.LeftPanel)
                            or nameof(MainWindowViewModel.RightPanel))
        {
            WirePanelPicker(vm.LeftPanel);
            WirePanelPicker(vm.RightPanel);
        }

        if (e.PropertyName != nameof(MainWindowViewModel.IsSingleView)) return;

        // Column indices: 0=left, 1=divider, 2=right
        if (vm.IsSingleView)
        {
            PanelsGrid.ColumnDefinitions[1].Width = new GridLength(0);
            PanelsGrid.ColumnDefinitions[2].Width = new GridLength(0);
        }
        else
        {
            PanelsGrid.ColumnDefinitions[1].Width = new GridLength(4);
            PanelsGrid.ColumnDefinitions[2].Width = GridLength.Star;
        }
    }

    private async Task<string?> PickFolderAsync()
    {
        var results = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { AllowMultiple = false });
        return results is { Count: > 0 } ? results[0].TryGetLocalPath() : null;
    }

    private Task ShowAlertAsync(string title, string message)
    {
        var dialog = new MessageDialog(title, message);
        return dialog.ShowDialog(this);
    }

    private async Task<bool> ConfirmAsync(string title, string message)
    {
        var dialog = new ConfirmDialog(title, message);
        await dialog.ShowDialog(this);
        return dialog.Result;
    }

    private void WirePanelPicker(ImagePanelViewModel? panel)
    {
        if (panel is null) return;
        panel.ShowPickerAsync = idx => ShowPickerDialogAsync(panel, idx);
    }

    private async Task<int?> ShowPickerDialogAsync(ImagePanelViewModel panel, int currentIndex)
    {
        var dialog = new ThumbnailPickerDialog(panel.Images, currentIndex);
        await dialog.ShowDialog(this);
        return dialog.SelectedIndex;
    }
}