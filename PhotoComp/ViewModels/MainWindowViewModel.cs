using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoComp.Converters;
using PhotoComp.Models;
using PhotoComp.Services;
using System.ComponentModel;

namespace PhotoComp.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    // Injected by the View after construction so the VM stays testable without Avalonia.
    public Func<Task<string?>>? PickSourceFolderAsync { get; set; }
    public Func<Task<string?>>? PickDestFolderAsync { get; set; }
    /// <summary>Shows a modal alert dialog. Injected by the View; null-safe (no-ops in tests).</summary>
    public Func<string, string, Task>? ShowAlertAsync { get; set; }
    /// <summary>Shows a yes/no confirmation dialog. Returns false when null (safe default).</summary>
    public Func<string, string, string, Task<bool>>? ConfirmAsync { get; set; }
    /// <summary>Shows the full-screen compare window for two images. Injected by the View; null-safe (no-ops in tests).</summary>
    public Func<ImageItem, ImageItem, Task>? ShowCompareAsync { get; set; }
    public ZoomState SharedZoom { get; } = new();
    private readonly HashSet<string> _selectedPaths = [];

    private bool _favoritesAreSaved;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedCount))]
    [NotifyCanExecuteChangedFor(nameof(CopySelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveSelectedCommand))]
    private IReadOnlyList<ImageItem> _images = [];

    // Backing list for Images: kept as a plain List so newly-detected files can be inserted
    // in place (Images wraps this same list via AsReadOnly(), so existing panels/filmstrip
    // see the change live without needing to be recreated).
    private List<ImageItem> _imagesList = [];
    private System.IO.FileSystemWatcher? _folderWatcher;

    private void SetImages(List<ImageItem> items)
    {
        _imagesList = items;
        Images = _imagesList.AsReadOnly();
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PushLeftToRightCommand))]
    [NotifyCanExecuteChangedFor(nameof(PushRightToLeftCommand))]
    [NotifyCanExecuteChangedFor(nameof(CompareImagesCommand))]
    [NotifyPropertyChangedFor(nameof(IsLeftPanelActive))]
    private ImagePanelViewModel? _leftPanel;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PushLeftToRightCommand))]
    [NotifyCanExecuteChangedFor(nameof(PushRightToLeftCommand))]
    [NotifyCanExecuteChangedFor(nameof(CompareImagesCommand))]
    [NotifyPropertyChangedFor(nameof(IsRightPanelActive))]
    private ImagePanelViewModel? _rightPanel;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _currentFolder;

    public int SelectedCount => _selectedPaths.Count;

    public bool HasSelections => _selectedPaths.Count > 0;

    [RelayCommand]
    private async Task LoadFolder()
    {
        if (PickSourceFolderAsync is null) return;
        var folder = await PickSourceFolderAsync();
        if (string.IsNullOrWhiteSpace(folder)) return;
        await LoadFolderFromPath(folder);
    }

    public async Task LoadFolderFromPath(string folder, string? initialFilePath = null)
    {
        if (_selectedPaths.Count > 0 && !_favoritesAreSaved)
        {
            var count = _selectedPaths.Count;
            var noun = count == 1 ? "1 favorited image" : $"{count} favorited images";
            var confirmed = await (ConfirmAsync?.Invoke(
                "Switch Folder?",
                $"You have {noun}. Opening a new folder will clear all favorites.\n\nContinue?",
                "Continue")
                ?? Task.FromResult(true));
            if (!confirmed) return;
        }

        IsLoading = true;
        StringToBitmapConverter.ClearCache();
        ThumbnailItemViewModel.ClearCache();
        try
        {
            var loaded = await ImageLoaderService.LoadImagesAsync(folder);
            SetImages(loaded.ToList());
            _selectedPaths.Clear();

            int leftIdx = 0;
            if (initialFilePath is not null)
            {
                for (int i = 0; i < loaded.Count; i++)
                {
                    if (string.Equals(loaded[i].FilePath, initialFilePath, StringComparison.OrdinalIgnoreCase))
                    {
                        leftIdx = i;
                        break;
                    }
                }
            }

            LeftPanel  = CreatePanel(leftIdx);
            RightPanel = CreatePanel(Images.Count > 1 ? (leftIdx == 0 ? 1 : 0) : 0);
            SetActivePanel(LeftPanel);
            RebuildFilmstrip(Images, leftIdx);
            CurrentFolder = folder;
            SetupFolderWatcher(folder);

            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(HasSelections));
            CopySelectedCommand.NotifyCanExecuteChanged();
            MoveSelectedCommand.NotifyCanExecuteChanged();
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelections))]
    private async Task CopySelected()
    {
        if (PickDestFolderAsync is null) return;
        var dest = await PickDestFolderAsync();
        if (string.IsNullOrWhiteSpace(dest)) return;

        var result = CopyService.CopySelected(_selectedPaths, dest);

        var title = result.HasFailures ? "Copy — Errors Occurred" : "Copy Complete";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Copied:  {result.Copied}");
        if (result.Skipped > 0)
            sb.AppendLine($"Skipped (already exists):  {result.Skipped}");
        if (result.HasFailures)
        {
            sb.AppendLine();
            sb.AppendLine($"Failed:  {result.Failures.Count}");
            foreach (var (fileName, error) in result.Failures)
                sb.AppendLine($"  • {fileName}: {error}");
        }

        if (!result.HasFailures)
            _favoritesAreSaved = true;

        await (ShowAlertAsync?.Invoke(title, sb.ToString().TrimEnd()) ?? Task.CompletedTask);
    }

    [RelayCommand(CanExecute = nameof(HasSelections))]
    private async Task MoveSelected()
    {
        if (PickDestFolderAsync is null) return;
        var dest = await PickDestFolderAsync();
        if (string.IsNullOrWhiteSpace(dest)) return;

        var result = MoveService.MoveSelected(_selectedPaths, dest);

        var title = result.HasFailures ? "Move — Errors Occurred" : "Move Complete";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Moved:   {result.Moved}");
        if (result.Skipped > 0)
            sb.AppendLine($"Skipped (already exists):  {result.Skipped}");
        if (result.HasFailures)
        {
            sb.AppendLine();
            sb.AppendLine($"Failed:  {result.Failures.Count}");
            foreach (var (fileName, error) in result.Failures)
                sb.AppendLine($"  \u2022 {fileName}: {error}");
        }

        // Remove successfully moved images from the list and selections.
        if (result.MovedSourcePaths.Count > 0)
        {
            var movedSet = new HashSet<string>(result.MovedSourcePaths, StringComparer.OrdinalIgnoreCase);
            foreach (var p in result.MovedSourcePaths)
                _selectedPaths.Remove(p);

            var newImages = Images.Where(i => !movedSet.Contains(i.FilePath)).ToList();
            var leftIdx  = Math.Min(LeftPanel?.CurrentIndex  ?? 0, Math.Max(0, newImages.Count - 1));
            var rightIdx = Math.Min(RightPanel?.CurrentIndex ?? 0, Math.Max(0, newImages.Count - 1));

            SetImages(newImages);
            LeftPanel  = newImages.Count > 0 ? CreatePanel(leftIdx)  : null;
            RightPanel = newImages.Count > 0 ? CreatePanel(rightIdx) : null;
            SetActivePanel(LeftPanel);
            RebuildFilmstrip(newImages, leftIdx);

            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(HasSelections));
            CopySelectedCommand.NotifyCanExecuteChanged();
            MoveSelectedCommand.NotifyCanExecuteChanged();

            if (!result.HasFailures)
                _favoritesAreSaved = true;
        }

        await (ShowAlertAsync?.Invoke(title, sb.ToString().TrimEnd()) ?? Task.CompletedTask);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDualView))]
    [NotifyPropertyChangedFor(nameof(SingleViewButtonIcon))]
    [NotifyPropertyChangedFor(nameof(SingleViewButtonLabel))]
    [NotifyCanExecuteChangedFor(nameof(CompareImagesCommand))]
    private bool _isSingleView;

    public bool IsDualView => !IsSingleView;
    public string SingleViewButtonIcon => IsSingleView ? "⊞" : "⊟";
    public string SingleViewButtonLabel => IsSingleView ? "Dual View" : "Single View";

    [RelayCommand]
    private void ToggleSingleView() => IsSingleView = !IsSingleView;

    [RelayCommand]
    private void ResetZoom() => SharedZoom.Reset();

    private bool CanPushPanels => LeftPanel is not null && RightPanel is not null;

    public bool CanCompareImages =>
        IsDualView
        && LeftPanel?.CurrentImage is not null
        && RightPanel?.CurrentImage is not null
        && !string.Equals(LeftPanel.CurrentImage.FileName, RightPanel.CurrentImage.FileName, StringComparison.OrdinalIgnoreCase);

    [RelayCommand(CanExecute = nameof(CanCompareImages))]
    private async Task CompareImages()
    {
        if (LeftPanel?.CurrentImage is null || RightPanel?.CurrentImage is null) return;
        if (ShowCompareAsync is not null)
        {
            await ShowCompareAsync(LeftPanel.CurrentImage, RightPanel.CurrentImage);
        }
    }

    [RelayCommand(CanExecute = nameof(CanPushPanels))]
    private void PushLeftToRight()
    {
        if (LeftPanel is null || RightPanel is null) return;
        RightPanel.CurrentIndex = LeftPanel.CurrentIndex;
    }

    [RelayCommand(CanExecute = nameof(CanPushPanels))]
    private void PushRightToLeft()
    {
        if (LeftPanel is null || RightPanel is null) return;
        LeftPanel.CurrentIndex = RightPanel.CurrentIndex;
    }

    private ImagePanelViewModel CreatePanel(int startIndex)
    {
        var vm = new ImagePanelViewModel(Images, SharedZoom, _selectedPaths, startIndex);
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ImagePanelViewModel.CurrentIndex) or nameof(ImagePanelViewModel.CurrentImage))
                CompareImagesCommand.NotifyCanExecuteChanged();
        };
        vm.HeartToggled += (_, _) =>
        {
            _favoritesAreSaved = false;
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(HasSelections));
            CopySelectedCommand.NotifyCanExecuteChanged();
            MoveSelectedCommand.NotifyCanExecuteChanged();
            if (_filmstripItems is not null)
                foreach (var fi in _filmstripItems)
                    fi.IsHearted = _selectedPaths.Contains(fi.FilePath);
        };
        vm.RequestDeleteAsync = DeleteImageAsync;
        return vm;
    }

    public async Task DeleteImageAsync(ImageItem item)
    {
        var sidecars = SidecarService.FindSidecars(item.FilePath);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Permanently delete:");
        sb.AppendLine($"  {item.FileName}");
        if (sidecars.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("The following sidecar files will also be deleted:");
            foreach (var s in sidecars)
                sb.AppendLine($"  \u2022 {System.IO.Path.GetFileName(s)}");
        }
        sb.AppendLine();
        sb.Append("This cannot be undone.");

        var confirmed = await (ConfirmAsync?.Invoke("Delete Image?", sb.ToString(), "Delete") ?? Task.FromResult(false));
        if (!confirmed) return;

        var result = DeleteService.Delete(item.FilePath);
        if (!result.Deleted)
        {
            await (ShowAlertAsync?.Invoke("Delete Failed", result.Error ?? "Unknown error.") ?? Task.CompletedTask);
            return;
        }

        // Rebuild the image list without the deleted item.
        var newImages = Images.Where(i => i.FilePath != item.FilePath).ToList();
        _selectedPaths.Remove(item.FilePath);

        // Clamp each panel's current index to the new list bounds.
        var leftIdx  = Math.Min(LeftPanel?.CurrentIndex  ?? 0, Math.Max(0, newImages.Count - 1));
        var rightIdx = Math.Min(RightPanel?.CurrentIndex ?? 0, Math.Max(0, newImages.Count - 1));

        SetImages(newImages);
        LeftPanel  = newImages.Count > 0 ? CreatePanel(leftIdx)  : null;
        RightPanel = newImages.Count > 0 ? CreatePanel(rightIdx) : null;
        SetActivePanel(LeftPanel);
        RebuildFilmstrip(newImages, leftIdx);

        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelections));
        CopySelectedCommand.NotifyCanExecuteChanged();
        MoveSelectedCommand.NotifyCanExecuteChanged();
    }

    // ── Filmstrip ─────────────────────────────────────────────────────────────────────────────

    [ObservableProperty] private bool _isFilmstripVisible;

    [RelayCommand]
    private void ToggleFilmstrip() => IsFilmstripVisible = !IsFilmstripVisible;

    private List<ThumbnailItemViewModel>? _filmstripItems;
    private CancellationTokenSource?     _filmstripCts;
    private ImagePanelViewModel?         _activePanelSub;

    public IReadOnlyList<ThumbnailItemViewModel>? FilmstripItems => _filmstripItems?.AsReadOnly();

    private ImagePanelViewModel? _activePanel;
    public ImagePanelViewModel? ActivePanel => _activePanel;

    /// <summary>True when the left panel is the active target for filmstrip navigation.</summary>
    public bool IsLeftPanelActive  => ReferenceEquals(_activePanel, LeftPanel)  && LeftPanel  is not null;
    /// <summary>True when the right panel is the active target for filmstrip navigation.</summary>
    public bool IsRightPanelActive => ReferenceEquals(_activePanel, RightPanel) && RightPanel is not null;

    /// <summary>Activates <paramref name="panel"/> as the filmstrip navigation target.</summary>
    public void SetActivePanel(ImagePanelViewModel? panel)
    {
        if (ReferenceEquals(_activePanel, panel)) return;
        _activePanel = panel;
        TrackActivePanel(panel);
        OnPropertyChanged(nameof(ActivePanel));
        OnPropertyChanged(nameof(IsLeftPanelActive));
        OnPropertyChanged(nameof(IsRightPanelActive));
    }

    private void TrackActivePanel(ImagePanelViewModel? panel)
    {
        if (_activePanelSub is not null)
            _activePanelSub.PropertyChanged -= OnActivePanelSubChanged;
        _activePanelSub = panel;
        if (panel is not null)
            panel.PropertyChanged += OnActivePanelSubChanged;
        SyncFilmstripActive(panel?.CurrentIndex ?? 0);
    }

    private void OnActivePanelSubChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ImagePanelViewModel.CurrentIndex) &&
            sender is ImagePanelViewModel panel)
            SyncFilmstripActive(panel.CurrentIndex);
    }

    private void SyncFilmstripActive(int index)
    {
        if (_filmstripItems is null) return;
        foreach (var item in _filmstripItems)
            item.IsCurrentImage = item.Index == index;
    }

    private void RebuildFilmstrip(IReadOnlyList<ImageItem> images, int currentIndex)
    {
        _filmstripCts?.Cancel();
        _filmstripCts?.Dispose();
        _filmstripCts = new CancellationTokenSource();

        _filmstripItems = images
            .Select((img, i) => new ThumbnailItemViewModel(
                index:          i,
                filePath:       img.FilePath,
                fileName:       img.FileName,
                isCurrentImage: i == currentIndex,
                isHearted:      _selectedPaths.Contains(img.FilePath)))
            .ToList();

        OnPropertyChanged(nameof(FilmstripItems));

        var token = _filmstripCts.Token;
        foreach (var item in _filmstripItems)
            _ = item.LoadAsync(token);
    }

    /// <summary>
    /// Navigates the active panel (falling back to left) to the clicked filmstrip item.
    /// Called by <see cref="MainWindow"/> in response to a pointer press on the filmstrip strip.
    /// </summary>
    public void FilmstripItemClicked(ThumbnailItemViewModel item)
    {
        var panel = _activePanel ?? LeftPanel;
        if (panel is null || Images.Count == 0) return;
        panel.CurrentIndex = Math.Clamp(item.Index, 0, Images.Count - 1);
    }

    // ── Folder watching ──────────────────────────────────────────────────────────────────────

    private void SetupFolderWatcher(string folder)
    {
        _folderWatcher?.Dispose();
        _folderWatcher = new System.IO.FileSystemWatcher(folder)
        {
            NotifyFilter = System.IO.NotifyFilters.FileName | System.IO.NotifyFilters.LastWrite,
            IncludeSubdirectories = false,
        };
        _folderWatcher.Created += OnFileCreatedInFolder;
        _folderWatcher.EnableRaisingEvents = true;
    }

    private void OnFileCreatedInFolder(object sender, System.IO.FileSystemEventArgs e)
    {
        if (!ImageLoaderService.IsSupportedExtension(e.FullPath)) return;
        _ = HandleNewImageFileAsync(e.FullPath);
    }

    /// <summary>
    /// Waits for the file to become readable (it may still be mid-write by whatever
    /// process created it), reads its metadata, then inserts it in date-sorted order.
    /// Fires on a FileSystemWatcher thread pool thread, so the insert is marshalled
    /// back to the UI thread.
    /// </summary>
    private async Task HandleNewImageFileAsync(string filePath)
    {
        // Give the generating tool a head start before we touch the file at all — some
        // image generators save in multiple steps (pixels, then metadata) and can throw
        // a permission error if we open the file for reading in between.
        await Task.Delay(NewFileInitialDelayMs).ConfigureAwait(false);

        if (!await WaitUntilFileReadyAsync(filePath)) return;

        ImageItem newItem;
        try
        {
            newItem = await ImageLoaderService.LoadSingleImageAsync(filePath);
        }
        catch
        {
            return; // vanished, unreadable, or unsupported despite the extension match
        }

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => InsertNewImage(newItem));
    }

    private const int NewFileInitialDelayMs = 1500;

    /// <summary>
    /// Waits for the file to become readable AND for its size to stop changing across
    /// consecutive checks — being openable isn't enough, since some generators write
    /// pixels and metadata in separate passes and a mid-write read would grab a
    /// truncated image with incomplete metadata.
    /// </summary>
    private static async Task<bool> WaitUntilFileReadyAsync(string filePath, int maxAttempts = 30, int delayMs = 300)
    {
        long lastSize = -1;
        int stableCount = 0;

        for (int i = 0; i < maxAttempts; i++)
        {
            try
            {
                var info = new System.IO.FileInfo(filePath);
                if (!info.Exists) return false;

                // ReadWrite sharing so our probe never blocks the generating tool's own writes.
                using (System.IO.File.Open(
                    filePath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
                {
                }

                if (info.Length > 0 && info.Length == lastSize)
                {
                    if (++stableCount >= 2) return true; // same size across two checks — write has settled
                }
                else
                {
                    stableCount = 0;
                }
                lastSize = info.Length;
            }
            catch (System.IO.FileNotFoundException)
            {
                return false; // removed again before we got to it
            }
            catch (System.IO.IOException)
            {
                stableCount = 0;
            }

            await Task.Delay(delayMs);
        }
        return false;
    }

    internal void InsertNewImage(ImageItem item)
    {
        // Guard against duplicate FileSystemWatcher events for the same file.
        if (_imagesList.Any(i => string.Equals(i.FilePath, item.FilePath, StringComparison.OrdinalIgnoreCase)))
            return;

        var insertIndex = _imagesList.FindIndex(i => i.DateTaken > item.DateTaken);
        if (insertIndex < 0) insertIndex = _imagesList.Count;
        _imagesList.Insert(insertIndex, item);
        OnPropertyChanged(nameof(Images));

        foreach (var panel in new[] { LeftPanel, RightPanel })
        {
            if (panel is null) continue;
            var newIndex = panel.CurrentIndex >= insertIndex ? panel.CurrentIndex + 1 : panel.CurrentIndex;
            panel.RefreshAfterImagesChanged(newIndex);
        }

        RebuildFilmstrip(Images, LeftPanel?.CurrentIndex ?? 0);
    }
}
