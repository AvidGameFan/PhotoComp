using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using PhotoComp.Models;
using PhotoComp.ViewModels;

namespace PhotoComp.Views;

public partial class ThumbnailPickerDialog : Window
{
    /// <summary>The index chosen by the user, or null if the dialog was cancelled.</summary>
    public int? SelectedIndex { get; private set; }

    private List<ThumbnailItemViewModel>? _items;
    private CancellationTokenSource?     _cts;

    // Parameterless ctor required by Avalonia's XAML runtime loader.
    public ThumbnailPickerDialog()
    {
        InitializeComponent();

        CloseButton.Click += (_, _) => Close();

        // Bubble-phase handler so clicks on any child (Image, TextBlock, Border) are caught.
        TheList.AddHandler(PointerPressedEvent, OnGridPointerPressed,
                           RoutingStrategies.Bubble);

        Opened += OnDialogOpened;
    }

    public ThumbnailPickerDialog(IReadOnlyList<ImageItem> images, int currentIndex,
                                  IReadOnlySet<string>? selectedPaths = null) : this()
    {
        _items = images
            .Select((img, i) => new ThumbnailItemViewModel(
                index:          i,
                filePath:       img.FilePath,
                fileName:       img.FileName,
                isCurrentImage: i == currentIndex,
                isHearted:      selectedPaths?.Contains(img.FilePath) ?? false))
            .ToList();

        TheList.ItemsSource = _items;

        HeaderText.Text = $"{images.Count} image{(images.Count == 1 ? "" : "s")} — " +
                          "click a thumbnail to navigate to it";
    }

    private async void OnDialogOpened(object? sender, EventArgs e)
    {
        if (_items is null) return;

        // Wait for UniformGridLayout to complete its first measure/arrange pass,
        // then scroll so the current item is visible.
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        var currentItem = _items.FirstOrDefault(x => x.IsCurrentImage);
        if (currentItem is not null)
            ScrollToItem(currentItem.Index);

        // Fire-and-forget thumbnail loading; each LoadAsync call is internally throttled.
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        foreach (var item in _items)
            _ = item.LoadAsync(token);
    }

    /// <summary>
    /// Estimates the scroll offset for <paramref name="index"/> and applies it.
    /// UniformGridLayout does not expose a "scroll to index" API, so we calculate
    /// from the known cell dimensions set in the XAML.
    /// </summary>
    private void ScrollToItem(int index)
    {
        const double cellWidth  = 204; // 196 item + 4 margin each side
        const double cellHeight = 236; // 228 item + 4 margin each side

        // ItemsControl has Margin="8"; subtract it from both sides to get WrapPanel width.
        double panelWidth = TheScroller.Bounds.Width - 16;
        if (panelWidth <= 0) return;

        int    cols = Math.Max(1, (int)(panelWidth / cellWidth));
        int    row  = index / cols;
        double y    = row * cellHeight + 8; // +8 = ItemsControl.Margin top

        // Scroll so the target row is near the top, with one row of context above.
        TheScroller.Offset = new Avalonia.Vector(0, Math.Max(0, y - cellHeight));
    }

    private void OnGridPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Walk up from the source element to find the item's DataContext.
        var element = e.Source as Control;
        while (element is not null && !ReferenceEquals(element, TheList))
        {
            if (element.DataContext is ThumbnailItemViewModel vm)
            {
                SelectedIndex = vm.Index;
                Close();
                return;
            }
            element = element.Parent as Control;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        base.OnClosed(e);
    }
}
