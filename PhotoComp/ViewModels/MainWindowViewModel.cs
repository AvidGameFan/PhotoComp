using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoComp.Models;
using PhotoComp.Services;

namespace PhotoComp.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    // Injected by the View after construction so the VM stays testable without Avalonia.
    public Func<Task<string?>>? PickSourceFolderAsync { get; set; }
    public Func<Task<string?>>? PickDestFolderAsync { get; set; }    /// <summary>Shows a modal alert dialog. Injected by the View; null-safe (no-ops in tests).</summary>
    public Func<string, string, Task>? ShowAlertAsync { get; set; }
    public ZoomState SharedZoom { get; } = new();
    private readonly HashSet<string> _selectedPaths = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedCount))]
    [NotifyCanExecuteChangedFor(nameof(CopySelectedCommand))]
    private IReadOnlyList<ImageItem> _images = [];

    [ObservableProperty] private ImagePanelViewModel? _leftPanel;
    [ObservableProperty] private ImagePanelViewModel? _rightPanel;
    [ObservableProperty] private bool _isLoading;

    public int SelectedCount => _selectedPaths.Count;

    public bool HasSelections => _selectedPaths.Count > 0;

    [RelayCommand]
    private async Task LoadFolder()
    {
        if (PickSourceFolderAsync is null) return;
        var folder = await PickSourceFolderAsync();
        if (string.IsNullOrWhiteSpace(folder)) return;

        IsLoading = true;
        try
        {
            var loaded = await ImageLoaderService.LoadImagesAsync(folder);
            Images = loaded;
            _selectedPaths.Clear();

            LeftPanel = CreatePanel(0);
            RightPanel = CreatePanel(Images.Count > 1 ? 1 : 0);

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

    private ImagePanelViewModel CreatePanel(int startIndex)
    {
        var vm = new ImagePanelViewModel(Images, SharedZoom, _selectedPaths, startIndex);
        vm.HeartToggled += (_, _) =>
        {
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(HasSelections));
            CopySelectedCommand.NotifyCanExecuteChanged();
        };
        return vm;
    }
}
