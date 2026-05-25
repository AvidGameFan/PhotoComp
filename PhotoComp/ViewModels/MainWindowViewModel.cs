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
    public Func<string, string, Task<bool>>? ConfirmAsync { get; set; }
    public ZoomState SharedZoom { get; } = new();
    private readonly HashSet<string> _selectedPaths = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedCount))]
    [NotifyCanExecuteChangedFor(nameof(CopySelectedCommand))]
    private IReadOnlyList<ImageItem> _images = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PushLeftToRightCommand))]
    [NotifyCanExecuteChangedFor(nameof(PushRightToLeftCommand))]
    [NotifyPropertyChangedFor(nameof(IsLeftPanelActive))]
    private ImagePanelViewModel? _leftPanel;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PushLeftToRightCommand))]
    [NotifyCanExecuteChangedFor(nameof(PushRightToLeftCommand))]
    [NotifyPropertyChangedFor(nameof(IsRightPanelActive))]
    private ImagePanelViewModel? _rightPanel;
    [ObservableProperty] private bool _isLoading;

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
        IsLoading = true;
        StringToBitmapConverter.ClearCache();
        ThumbnailItemViewModel.ClearCache();
        try
        {
            var loaded = await ImageLoaderService.LoadImagesAsync(folder);
            Images = loaded;
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

            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(HasSelections));
            CopySelectedCommand.NotifyCanExecuteChanged();
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

        await (ShowAlertAsync?.Invoke(title, sb.ToString().TrimEnd()) ?? Task.CompletedTask);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDualView))]
    [NotifyPropertyChangedFor(nameof(SingleViewButtonLabel))]
    private bool _isSingleView;

    public bool IsDualView => !IsSingleView;
    public string SingleViewButtonLabel => IsSingleView ? "⊞ Dual View" : "⊟ Single View";

    [RelayCommand]
    private void ToggleSingleView() => IsSingleView = !IsSingleView;

    [RelayCommand]
    private void ResetZoom() => SharedZoom.Reset();

    private bool CanPushPanels => LeftPanel is not null && RightPanel is not null;

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
        vm.HeartToggled += (_, _) =>
        {
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(HasSelections));
            CopySelectedCommand.NotifyCanExecuteChanged();
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

        var confirmed = await (ConfirmAsync?.Invoke("Delete Image?", sb.ToString()) ?? Task.FromResult(false));
        if (!confirmed) return;

        var result = DeleteService.Delete(item.FilePath);
        if (!result.Deleted)
        {
            await (ShowAlertAsync?.Invoke("Delete Failed", result.Error ?? "Unknown error.") ?? Task.CompletedTask);
            return;
        }

        // Rebuild the image list without the deleted item.
        var newImages = Images.Where(i => i.FilePath != item.FilePath).ToList().AsReadOnly();
        _selectedPaths.Remove(item.FilePath);

        // Clamp each panel's current index to the new list bounds.
        var leftIdx  = Math.Min(LeftPanel?.CurrentIndex  ?? 0, Math.Max(0, newImages.Count - 1));
        var rightIdx = Math.Min(RightPanel?.CurrentIndex ?? 0, Math.Max(0, newImages.Count - 1));

        Images = newImages;
        LeftPanel  = newImages.Count > 0 ? CreatePanel(leftIdx)  : null;
        RightPanel = newImages.Count > 0 ? CreatePanel(rightIdx) : null;
        SetActivePanel(LeftPanel);
        RebuildFilmstrip(newImages, leftIdx);

        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelections));
        CopySelectedCommand.NotifyCanExecuteChanged();
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
}
