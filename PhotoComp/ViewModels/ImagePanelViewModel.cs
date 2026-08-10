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
    [NotifyPropertyChangedFor(nameof(ExifDetailRows))]
    [NotifyCanExecuteChangedFor(nameof(CopyToClipboardCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyPromptCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowAiCriticCommand))]
    private int _currentIndex;

    [ObservableProperty] private bool _isExifVisible;

    [RelayCommand]
    private void ToggleExifOverlay() => IsExifVisible = !IsExifVisible;

    partial void OnCurrentIndexChanged(int value)
    {
        // If the newly displayed image has no EXIF data, close the overlay.
        if (IsExifVisible && ExifDetailRows is null)
            IsExifVisible = false;
    }

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

    /// <summary>Rows for the EXIF detail overlay. Null when no data is available.</summary>
    public IReadOnlyList<ExifRow>? ExifDetailRows
    {
        get
        {
            var img = CurrentImage;
            if (img is null) return null;

            var rows = new List<ExifRow>();
            var d = img.ExifDetails;

            if (!string.IsNullOrEmpty(d?.CameraModel))
            {
                var cam = (!string.IsNullOrEmpty(d.CameraMake) &&
                           !d.CameraModel.StartsWith(d.CameraMake, StringComparison.OrdinalIgnoreCase))
                    ? $"{d.CameraMake} {d.CameraModel}" : d.CameraModel!;
                rows.Add(new ExifRow("Camera", cam));
            }
            if (!string.IsNullOrEmpty(d?.LensModel))
            {
                var lens = (!string.IsNullOrEmpty(d.LensMake) &&
                            !d.LensModel.StartsWith(d.LensMake, StringComparison.OrdinalIgnoreCase))
                    ? $"{d.LensMake} {d.LensModel}" : d.LensModel!;
                rows.Add(new ExifRow("Lens", lens));
            }
            if (!string.IsNullOrEmpty(d?.ShutterSpeed))  rows.Add(new ExifRow("Shutter",       d.ShutterSpeed!));
            if (!string.IsNullOrEmpty(d?.Aperture))       rows.Add(new ExifRow("Aperture",      d.Aperture!));
            if (!string.IsNullOrEmpty(d?.Iso))            rows.Add(new ExifRow("ISO",           d.Iso!));
            if (!string.IsNullOrEmpty(d?.FocalLength))    rows.Add(new ExifRow("Focal Length",  d.FocalLength!));
            if (!string.IsNullOrEmpty(d?.FocalLength35mm)) rows.Add(new ExifRow("35mm Equiv.",  d.FocalLength35mm!));
            if (!string.IsNullOrEmpty(d?.ExposureBias))   rows.Add(new ExifRow("Exp. Bias",    d.ExposureBias!));
            if (!string.IsNullOrEmpty(d?.ExposureProgram)) rows.Add(new ExifRow("Program",     d.ExposureProgram!));
            if (!string.IsNullOrEmpty(d?.MeteringMode))   rows.Add(new ExifRow("Metering",     d.MeteringMode!));
            if (!string.IsNullOrEmpty(d?.Flash))          rows.Add(new ExifRow("Flash",         d.Flash!));
            if (!string.IsNullOrEmpty(d?.WhiteBalance))   rows.Add(new ExifRow("White Balance", d.WhiteBalance!));

            if (!string.IsNullOrEmpty(img.Prompt))
            {
                rows.Add(new ExifRow("Type", "AI Generated"));
                var ai = img.AiDetails;
                if (!string.IsNullOrEmpty(ai?.Model))          rows.Add(new ExifRow("Model",     ai.Model!));
                if (!string.IsNullOrEmpty(ai?.VaeModel))        rows.Add(new ExifRow("VAE",       ai.VaeModel!));
                if (!string.IsNullOrEmpty(ai?.Sampler))         rows.Add(new ExifRow("Sampler",   ai.Sampler!));
                if (!string.IsNullOrEmpty(ai?.Scheduler))       rows.Add(new ExifRow("Scheduler", ai.Scheduler!));
                if (!string.IsNullOrEmpty(ai?.GuidanceScale))   rows.Add(new ExifRow("Guidance",  ai.GuidanceScale!));
                if (!string.IsNullOrEmpty(ai?.Seed))            rows.Add(new ExifRow("Seed",      ai.Seed!));
                if (!string.IsNullOrEmpty(ai?.NegativePrompt))  rows.Add(new ExifRow("Negative",  ai.NegativePrompt!));
            }

            return rows.Count > 0 ? rows : null;
        }
    }

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
    /// Injected by the view layer to copy text to the system clipboard.
    /// Receives the text string; null-safe (no-op when not set).
    /// </summary>
    public Func<string, Task>? CopyTextAsync { get; set; }

    [RelayCommand(CanExecute = nameof(CanCopyPrompt))]
    private async Task CopyPrompt()
    {
        if (PromptText is null || CopyTextAsync is null) return;
        await CopyTextAsync(PromptText);
    }

    private bool CanCopyPrompt() => !string.IsNullOrEmpty(PromptText);

    /// <summary>
    /// Raised when the heart is toggled so MainWindowViewModel can refresh SelectedCount.
    /// </summary>
    public event EventHandler? HeartToggled;

    /// <summary>
    /// Injected by the view layer to show the AI Critic dialog for the current image.
    /// Null-safe: no-op when not set (e.g. in unit tests).
    /// </summary>
    public Func<ImageItem, Task>? ShowAiCriticAsync { get; set; }

    [RelayCommand(CanExecute = nameof(CanShowAiCritic))]
    private async Task ShowAiCritic()
    {
        if (CurrentImage is null || ShowAiCriticAsync is null) return;
        await ShowAiCriticAsync(CurrentImage);
    }

    private bool CanShowAiCritic() => CurrentImage is not null;
}
