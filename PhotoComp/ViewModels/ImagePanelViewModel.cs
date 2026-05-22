using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoComp.Models;

namespace PhotoComp.ViewModels;

public sealed partial class ImagePanelViewModel : ViewModelBase
{
    private readonly IReadOnlyList<ImageItem> _images;
    private readonly HashSet<string> _selectedPaths;

    public ZoomState SharedZoom { get; }

    /// <summary>The full image list this panel is browsing.</summary>
    public IReadOnlyList<ImageItem> Images => _images;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentImage))]
    [NotifyPropertyChangedFor(nameof(IsCurrentHearted))]
    [NotifyPropertyChangedFor(nameof(HeartGlyph))]
    [NotifyPropertyChangedFor(nameof(PositionLabel))]
    [NotifyPropertyChangedFor(nameof(InfoText))]
    [NotifyPropertyChangedFor(nameof(PromptText))]
    [NotifyCanExecuteChangedFor(nameof(CopyToClipboardCommand))]
    private int _currentIndex;

    public ImagePanelViewModel(
        IReadOnlyList<ImageItem> images,
        ZoomState sharedZoom,
        HashSet<string> selectedPaths,
        int startIndex = 0)
    {
        _images = images;
        SharedZoom = sharedZoom;
        _selectedPaths = selectedPaths;
        CurrentIndex = images.Count > 0 ? Math.Clamp(startIndex, 0, images.Count - 1) : 0;
    }

    public ImageItem? CurrentImage =>
        _images.Count > 0 ? _images[CurrentIndex] : null;

    public bool IsCurrentHearted =>
        CurrentImage is not null && _selectedPaths.Contains(CurrentImage.FilePath);

    /// <summary>The shared set of hearted file paths (read-only view).</summary>
    public IReadOnlySet<string> SelectedPaths => _selectedPaths;

    public string PositionLabel =>
        _images.Count > 0 ? $"{CurrentIndex + 1} / {_images.Count}" : "—";

    /// <summary>Filled heart when selected, outline when not.</summary>
    public string HeartGlyph => IsCurrentHearted ? "\u2665" : "\u2661";

    /// <summary>Pixel dimensions and EXIF date for the overlay badge.</summary>
    public string InfoText => CurrentImage is null
        ? string.Empty
        : $"{CurrentImage.Width}\u00d7{CurrentImage.Height}  |  {CurrentImage.DateTaken:yyyy-MM-dd HH:mm:ss}";

    /// <summary>
    /// Text shown in the bottom-right overlay: SD generation prompt when present,
    /// otherwise a camera EXIF summary (ISO, aperture, shutter speed, focal length).
    /// Null when neither is available.
    /// </summary>
    public string? PromptText => CurrentImage?.Prompt ?? CurrentImage?.ExifCaption;

    [RelayCommand]
    public void NavigateNext()
    {
        if (_images.Count == 0) return;
        CurrentIndex = (CurrentIndex + 1) % _images.Count;
    }

    [RelayCommand]
    public void NavigatePrevious()
    {
        if (_images.Count == 0) return;
        CurrentIndex = (CurrentIndex - 1 + _images.Count) % _images.Count;
    }

    [RelayCommand]
    public void ToggleHeart()
    {
        if (CurrentImage is null) return;

        if (_selectedPaths.Contains(CurrentImage.FilePath))
            _selectedPaths.Remove(CurrentImage.FilePath);
        else
            _selectedPaths.Add(CurrentImage.FilePath);

        OnPropertyChanged(nameof(IsCurrentHearted));
        OnPropertyChanged(nameof(HeartGlyph));
        HeartToggled?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Injected by <see cref="MainWindowViewModel"/> to handle delete confirmation and
    /// execution. Null-safe: no-ops if not set (e.g. in construction-time design data).
    /// </summary>
    public Func<ImageItem, Task>? RequestDeleteAsync { get; set; }

    [RelayCommand]
    private async Task DeleteCurrent()
    {
        if (CurrentImage is null) return;
        await (RequestDeleteAsync?.Invoke(CurrentImage) ?? Task.CompletedTask);
    }

    /// <summary>
    /// Opens the thumbnail picker dialog. Injected by the View so the VM stays testable.
    /// Receives the current index; returns the chosen index, or null when cancelled.
    /// </summary>
    public Func<int, Task<int?>>? ShowPickerAsync { get; set; }

    [RelayCommand]
    private async Task ShowPicker()
    {
        if (ShowPickerAsync is null || _images.Count == 0) return;
        var chosen = await ShowPickerAsync(CurrentIndex);
        if (chosen.HasValue)
            CurrentIndex = Math.Clamp(chosen.Value, 0, _images.Count - 1);
    }

    /// <summary>
    /// Injected by the view layer to copy the current image to the system clipboard.
    /// Receives the image file path; null-safe (no-op when not set).
    /// </summary>
    public Func<string, Task>? CopyImageAsync { get; set; }

    [RelayCommand(CanExecute = nameof(CanCopyToClipboard))]
    private async Task CopyToClipboard()
    {
        if (CurrentImage is null || CopyImageAsync is null) return;
        await CopyImageAsync(CurrentImage.FilePath);
    }

    private bool CanCopyToClipboard() => CurrentImage is not null;

    /// <summary>
    /// Raised when the heart is toggled so MainWindowViewModel can refresh SelectedCount.
    /// </summary>
    public event EventHandler? HeartToggled;
}
