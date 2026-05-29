using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using PhotoComp.ViewModels;

namespace PhotoComp.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _lastVm;
    private ImagePanelViewModel? _filmstripScrollPanel;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        FilmstripList.AddHandler(PointerPressedEvent, OnFilmstripPointerPressed,
                                 RoutingStrategies.Bubble);
        LeftPanelContainer.AddHandler(PointerPressedEvent,
            (_, _) => { if (DataContext is MainWindowViewModel vm) vm.SetActivePanel(vm.LeftPanel); },
            RoutingStrategies.Bubble);
        RightPanelContainer.AddHandler(PointerPressedEvent,
            (_, _) => { if (DataContext is MainWindowViewModel vm) vm.SetActivePanel(vm.RightPanel); },
            RoutingStrategies.Bubble);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent,     OnDrop);
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Formats.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var storageItem = e.DataTransfer.Items
            .Select(i => i.TryGetRaw(DataFormat.File) as IStorageItem)
            .FirstOrDefault(i => i is not null);
        if (storageItem is null) return;

        var path = storageItem.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        string folder;
        string? initialFile = null;

        if (Directory.Exists(path))
        {
            folder = path;
        }
        else if (File.Exists(path))
        {
            folder      = Path.GetDirectoryName(path)!;
            initialFile = path;
        }
        else return;

        await vm.LoadFolderFromPath(folder, initialFile);
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

        if (e.PropertyName == nameof(MainWindowViewModel.ActivePanel))
            WireFilmstripScroll(vm.ActivePanel);

        if (e.PropertyName != nameof(MainWindowViewModel.IsSingleView)) return;

        // Column indices: 0=left, 1=divider, 2=right
        if (vm.IsSingleView)
        {
            PanelsGrid.ColumnDefinitions[1].Width = new GridLength(0);
            PanelsGrid.ColumnDefinitions[2].Width = new GridLength(0);
            vm.SetActivePanel(vm.LeftPanel);
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

    private async Task<bool> ConfirmAsync(string title, string message, string confirmLabel)
    {
        var dialog = new ConfirmDialog(title, message, confirmLabel);
        await dialog.ShowDialog(this);
        return dialog.Result;
    }

    private void WirePanelPicker(ImagePanelViewModel? panel)
    {
        if (panel is null) return;
        panel.ShowPickerAsync = idx => ShowPickerDialogAsync(panel, idx);
        panel.CopyImageAsync  = CopyImageToClipboardAsync;
        panel.CopyTextAsync   = CopyTextToClipboardAsync;
    }

    private async Task CopyTextToClipboardAsync(string text)
    {
        if (Clipboard is null) return;
        try
        {
            var item     = DataTransferItem.Create(DataFormat.Text, text);
            var transfer = new DataTransfer();
            transfer.Add(item);
            await Clipboard.SetDataAsync(transfer);
        }
        catch { }
    }

    private async Task CopyImageToClipboardAsync(string filePath)
    {
        if (Clipboard is null) return;
        try
        {
            var bmp      = new Bitmap(filePath);
            var item     = DataTransferItem.Create(DataFormat.Bitmap, bmp);
            var transfer = new DataTransfer();
            transfer.Add(item);
            await Clipboard.SetDataAsync(transfer);
        }
        catch { }
    }

    private async Task<int?> ShowPickerDialogAsync(ImagePanelViewModel panel, int currentIndex)
    {
        var dialog = new ThumbnailPickerDialog(panel.Images, currentIndex, panel.SelectedPaths);
        await dialog.ShowDialog(this);
        return dialog.SelectedIndex;
    }

    private void WireFilmstripScroll(ImagePanelViewModel? panel)
    {
        if (_filmstripScrollPanel is not null)
            _filmstripScrollPanel.PropertyChanged -= OnFilmstripScrollPanelChanged;
        _filmstripScrollPanel = panel;
        if (panel is not null)
            panel.PropertyChanged += OnFilmstripScrollPanelChanged;
    }

    private void OnFilmstripScrollPanelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ImagePanelViewModel.CurrentIndex)) return;
        if (sender is ImagePanelViewModel panel)
            ScrollFilmstripTo(panel.CurrentIndex);
    }

    private void ScrollFilmstripTo(int index)
    {
        const double cellWidth = 90; // 84px cell + 3px margin each side
        double targetCenter  = index * cellWidth + cellWidth / 2;
        double viewportWidth = FilmstripScroller.Viewport.Width;
        double offset        = Math.Max(0, targetCenter - viewportWidth / 2);
        FilmstripScroller.Offset = new Vector(offset, 0);
    }

    private void OnFilmstripPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var element = e.Source as Control;
        while (element is not null && !ReferenceEquals(element, FilmstripList))
        {
            if (element.DataContext is ThumbnailItemViewModel item)
            {
                vm.FilmstripItemClicked(item);
                return;
            }
            element = element.Parent as Control;
        }
    }
}