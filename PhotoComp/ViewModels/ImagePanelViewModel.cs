using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoComp.Models;

namespace PhotoComp.ViewModels;

public sealed partial class ImagePanelViewModel : ViewModelBase
{
    private readonly IReadOnlyList<ImageItem> _images;
    private readonly HashSet<string> _selectedPaths;

    public ZoomState SharedZoom { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentImage))]
    [NotifyPropertyChangedFor(nameof(IsCurrentHearted))]
    [NotifyPropertyChangedFor(nameof(HeartGlyph))]
    [NotifyPropertyChangedFor(nameof(PositionLabel))]
    [NotifyPropertyChangedFor(nameof(InfoText))]
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

    public string PositionLabel =>
        _images.Count > 0 ? $"{CurrentIndex + 1} / {_images.Count}" : "—";

    /// <summary>Filled heart when selected, outline when not.</summary>
    public string HeartGlyph => IsCurrentHearted ? "\u2665" : "\u2661";

    /// <summary>Pixel dimensions and EXIF date for the overlay badge.</summary>
    public string InfoText => CurrentImage is null
        ? string.Empty
        : $"{CurrentImage.Width}\u00d7{CurrentImage.Height}  |  {CurrentImage.DateTaken:yyyy-MM-dd HH:mm:ss}";

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
    /// Raised when the heart is toggled so MainWindowViewModel can refresh SelectedCount.
    /// </summary>
    public event EventHandler? HeartToggled;
}
