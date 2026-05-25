using PhotoComp.Models;
using PhotoComp.Services;
using PhotoComp.ViewModels;

namespace PhotoComp.Tests;

public class MainWindowViewModelTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public MainWindowViewModelTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    // Writes a minimal fake JPEG to the temp folder and sets its last-write time.
    private string WriteJpeg(string name, DateTime lastWrite)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, [0xFF, 0xD8, 0xFF, 0xE0]);
        File.SetLastWriteTime(path, lastWrite);
        return path;
    }

    // Builds a VM wired so LoadFolder reads from _tempDir.
    private MainWindowViewModel MakeVm()
    {
        var vm = new MainWindowViewModel();
        vm.PickSourceFolderAsync = () => Task.FromResult<string?>(_tempDir);
        vm.PickDestFolderAsync   = () => Task.FromResult<string?>(_tempDir);
        return vm;
    }

    // ── Initial state ─────────────────────────────────────────────────

    [Fact]
    public void InitialState_HasNoSelections()
    {
        var vm = new MainWindowViewModel();
        Assert.False(vm.HasSelections);
        Assert.Equal(0, vm.SelectedCount);
    }

    [Fact]
    public void InitialState_PanelsAreNull()
    {
        var vm = new MainWindowViewModel();
        Assert.Null(vm.LeftPanel);
        Assert.Null(vm.RightPanel);
    }

    // ── LoadFolder ────────────────────────────────────────────────────

    [Fact]
    public async Task LoadFolder_PopulatesBothPanels()
    {
        WriteJpeg("a.jpg", new DateTime(2024, 1, 1));
        WriteJpeg("b.jpg", new DateTime(2024, 2, 1));
        var vm = MakeVm();

        await vm.LoadFolderCommand.ExecuteAsync(null);

        Assert.NotNull(vm.LeftPanel);
        Assert.NotNull(vm.RightPanel);
    }

    [Fact]
    public async Task LoadFolder_LeftPanelStartsAtIndex0()
    {
        WriteJpeg("a.jpg", new DateTime(2024, 1, 1));
        WriteJpeg("b.jpg", new DateTime(2024, 2, 1));
        var vm = MakeVm();

        await vm.LoadFolderCommand.ExecuteAsync(null);

        Assert.Equal(0, vm.LeftPanel!.CurrentIndex);
    }

    [Fact]
    public async Task LoadFolder_RightPanelStartsAtIndex1_WhenMultipleImages()
    {
        WriteJpeg("a.jpg", new DateTime(2024, 1, 1));
        WriteJpeg("b.jpg", new DateTime(2024, 2, 1));
        var vm = MakeVm();

        await vm.LoadFolderCommand.ExecuteAsync(null);

        Assert.Equal(1, vm.RightPanel!.CurrentIndex);
    }

    [Fact]
    public async Task LoadFolder_RightPanelStartsAtIndex0_WhenSingleImage()
    {
        WriteJpeg("only.jpg", DateTime.Now);
        var vm = MakeVm();

        await vm.LoadFolderCommand.ExecuteAsync(null);

        Assert.Equal(0, vm.RightPanel!.CurrentIndex);
    }

    [Fact]
    public async Task LoadFolder_EmptyFolder_PanelsStillCreated()
    {
        var vm = MakeVm();

        await vm.LoadFolderCommand.ExecuteAsync(null);

        // Even with 0 images the panels are created so the UI is in a valid state
        Assert.NotNull(vm.LeftPanel);
        Assert.NotNull(vm.RightPanel);
    }

    [Fact]
    public async Task LoadFolder_ClearsExistingSelections()
    {
        WriteJpeg("a.jpg", new DateTime(2024, 1, 1));
        WriteJpeg("b.jpg", new DateTime(2024, 2, 1));
        var vm = MakeVm();

        // First load — heart an image
        await vm.LoadFolderCommand.ExecuteAsync(null);
        vm.LeftPanel!.ToggleHeartCommand.Execute(null);
        Assert.True(vm.HasSelections);

        // Second load — selections must be wiped
        await vm.LoadFolderCommand.ExecuteAsync(null);

        Assert.False(vm.HasSelections);
        Assert.Equal(0, vm.SelectedCount);
    }

    [Fact]
    public async Task LoadFolder_DoesNothing_WhenPickerReturnsNull()
    {
        var vm = new MainWindowViewModel();
        vm.PickSourceFolderAsync = () => Task.FromResult<string?>(null);

        await vm.LoadFolderCommand.ExecuteAsync(null);

        Assert.Null(vm.LeftPanel);
    }

    [Fact]
    public async Task LoadFolder_FiresPropertyChanged_ForHasSelections()
    {
        WriteJpeg("a.jpg", DateTime.Now);
        var vm = MakeVm();
        var fired = new List<string?>();
        vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        await vm.LoadFolderCommand.ExecuteAsync(null);

        Assert.Contains(nameof(vm.HasSelections), fired);
    }

    // ── HasSelections / SelectedCount via heart toggle ─────────────────

    [Fact]
    public async Task HasSelections_TrueAfterHeartingImage()
    {
        WriteJpeg("a.jpg", DateTime.Now);
        var vm = MakeVm();
        await vm.LoadFolderCommand.ExecuteAsync(null);

        vm.LeftPanel!.ToggleHeartCommand.Execute(null);

        Assert.True(vm.HasSelections);
        Assert.Equal(1, vm.SelectedCount);
    }

    [Fact]
    public async Task HasSelections_FalseAfterUnheartingImage()
    {
        WriteJpeg("a.jpg", DateTime.Now);
        var vm = MakeVm();
        await vm.LoadFolderCommand.ExecuteAsync(null);

        vm.LeftPanel!.ToggleHeartCommand.Execute(null); // add
        vm.LeftPanel!.ToggleHeartCommand.Execute(null); // remove

        Assert.False(vm.HasSelections);
        Assert.Equal(0, vm.SelectedCount);
    }

    [Fact]
    public async Task HeartToggle_FiresPropertyChanged_OnViewModel()
    {
        WriteJpeg("a.jpg", DateTime.Now);
        var vm = MakeVm();
        await vm.LoadFolderCommand.ExecuteAsync(null);

        var fired = new List<string?>();
        vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        vm.LeftPanel!.ToggleHeartCommand.Execute(null);

        Assert.Contains(nameof(vm.HasSelections), fired);
        Assert.Contains(nameof(vm.SelectedCount), fired);
    }

    [Fact]
    public async Task SelectedCount_AccumulatesAcrossBothPanels()
    {
        WriteJpeg("a.jpg", new DateTime(2024, 1, 1));
        WriteJpeg("b.jpg", new DateTime(2024, 2, 1));
        var vm = MakeVm();
        await vm.LoadFolderCommand.ExecuteAsync(null);

        // Heart left panel image 0, right panel image 1 — two distinct files
        vm.LeftPanel!.ToggleHeartCommand.Execute(null);
        vm.RightPanel!.ToggleHeartCommand.Execute(null);

        Assert.Equal(2, vm.SelectedCount);
    }

    // ── ResetZoomCommand ──────────────────────────────────────────────

    [Fact]
    public void ResetZoomCommand_ResetsSharedZoomToDefaults()
    {
        var vm = new MainWindowViewModel();
        vm.SharedZoom.Scale = 3.0;
        vm.SharedZoom.OffsetX = 100;
        vm.SharedZoom.OffsetY = -50;

        vm.ResetZoomCommand.Execute(null);

        Assert.Equal(1.0, vm.SharedZoom.Scale);
        Assert.Equal(0.0, vm.SharedZoom.OffsetX);
        Assert.Equal(0.0, vm.SharedZoom.OffsetY);
    }

    [Fact]
    public void ResetZoomCommand_IsAlwaysEnabled()
    {
        var vm = new MainWindowViewModel();
        Assert.True(vm.ResetZoomCommand.CanExecute(null));
    }

    // ── CopySelected command availability ─────────────────────────────

    [Fact]
    public void CopySelectedCommand_DisabledInitially()
    {
        var vm = new MainWindowViewModel();
        Assert.False(vm.CopySelectedCommand.CanExecute(null));
    }

    [Fact]
    public async Task CopySelectedCommand_EnabledAfterHearting()
    {
        WriteJpeg("a.jpg", DateTime.Now);
        var vm = MakeVm();
        await vm.LoadFolderCommand.ExecuteAsync(null);

        vm.LeftPanel!.ToggleHeartCommand.Execute(null);

        Assert.True(vm.CopySelectedCommand.CanExecute(null));
    }

    [Fact]
    public async Task CopySelectedCommand_CopiesSelectedFiles()
    {
        var srcPath = WriteJpeg("copy_me.jpg", DateTime.Now);
        var destDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var vm = MakeVm();
        vm.PickDestFolderAsync = () => Task.FromResult<string?>(destDir);

        try
        {
            await vm.LoadFolderCommand.ExecuteAsync(null);
            vm.LeftPanel!.ToggleHeartCommand.Execute(null);

            await vm.CopySelectedCommand.ExecuteAsync(null);

            Assert.True(File.Exists(Path.Combine(destDir, "copy_me.jpg")));
        }
        finally
        {
            if (Directory.Exists(destDir))
                Directory.Delete(destDir, recursive: true);
        }
    }

    [Fact]
    public async Task CopySelectedCommand_CopiesJsonSidecar_WhenPresent()
    {
        WriteJpeg("photo.jpg", DateTime.Now);
        File.WriteAllText(Path.Combine(_tempDir, "photo.json"), "{\"meta\":true}");
        var destDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var vm = MakeVm();
        vm.PickDestFolderAsync = () => Task.FromResult<string?>(destDir);

        try
        {
            await vm.LoadFolderCommand.ExecuteAsync(null);
            vm.LeftPanel!.ToggleHeartCommand.Execute(null);
            await vm.CopySelectedCommand.ExecuteAsync(null);

            Assert.True(File.Exists(Path.Combine(destDir, "photo.jpg")));
            Assert.True(File.Exists(Path.Combine(destDir, "photo.json")));
        }
        finally
        {
            if (Directory.Exists(destDir)) Directory.Delete(destDir, recursive: true);
        }
    }

    [Fact]
    public async Task CopySelectedCommand_CopiesTxtSidecar_WhenPresent()
    {
        WriteJpeg("photo.jpg", DateTime.Now);
        File.WriteAllText(Path.Combine(_tempDir, "photo.txt"), "some caption");
        var destDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var vm = MakeVm();
        vm.PickDestFolderAsync = () => Task.FromResult<string?>(destDir);

        try
        {
            await vm.LoadFolderCommand.ExecuteAsync(null);
            vm.LeftPanel!.ToggleHeartCommand.Execute(null);
            await vm.CopySelectedCommand.ExecuteAsync(null);

            Assert.True(File.Exists(Path.Combine(destDir, "photo.jpg")));
            Assert.True(File.Exists(Path.Combine(destDir, "photo.txt")));
        }
        finally
        {
            if (Directory.Exists(destDir)) Directory.Delete(destDir, recursive: true);
        }
    }

    [Fact]
    public async Task CopySelectedCommand_DoesNotCopySidecar_WhenAbsent()
    {
        WriteJpeg("photo.jpg", DateTime.Now);
        // No sidecar files created
        var destDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var vm = MakeVm();
        vm.PickDestFolderAsync = () => Task.FromResult<string?>(destDir);

        try
        {
            await vm.LoadFolderCommand.ExecuteAsync(null);
            vm.LeftPanel!.ToggleHeartCommand.Execute(null);
            await vm.CopySelectedCommand.ExecuteAsync(null);

            Assert.True(File.Exists(Path.Combine(destDir, "photo.jpg")));
            Assert.False(File.Exists(Path.Combine(destDir, "photo.json")));
            Assert.False(File.Exists(Path.Combine(destDir, "photo.txt")));
        }
        finally
        {
            if (Directory.Exists(destDir)) Directory.Delete(destDir, recursive: true);
        }
    }

    [Fact]
    public async Task CopySelectedCommand_DoesNothing_WhenPickerReturnsNull()
    {
        WriteJpeg("a.jpg", DateTime.Now);
        var vm = MakeVm();
        vm.PickDestFolderAsync = () => Task.FromResult<string?>(null);
        await vm.LoadFolderCommand.ExecuteAsync(null);
        vm.LeftPanel!.ToggleHeartCommand.Execute(null);

        var ex = await Record.ExceptionAsync(() => vm.CopySelectedCommand.ExecuteAsync(null));
        Assert.Null(ex);
    }

    // ── SharedZoom is shared with panels ──────────────────────────────

    [Fact]
    public async Task Panels_UseTheSameZoomStateInstance()
    {
        WriteJpeg("a.jpg", new DateTime(2024, 1, 1));
        WriteJpeg("b.jpg", new DateTime(2024, 2, 1));
        var vm = MakeVm();
        await vm.LoadFolderCommand.ExecuteAsync(null);

        Assert.Same(vm.SharedZoom, vm.LeftPanel!.SharedZoom);
        Assert.Same(vm.SharedZoom, vm.RightPanel!.SharedZoom);
    }

    // ── IsSingleView / ToggleSingleViewCommand ────────────────────────

    [Fact]
    public void InitialState_IsDualView()
    {
        var vm = new MainWindowViewModel();
        Assert.False(vm.IsSingleView);
        Assert.True(vm.IsDualView);
    }

    [Fact]
    public void InitialState_SingleViewButtonLabel_ShowsSingleViewOption()
    {
        var vm = new MainWindowViewModel();
        Assert.Equal("⊟ Single View", vm.SingleViewButtonLabel);
    }

    [Fact]
    public void ToggleSingleView_SetsIsSingleViewTrue()
    {
        var vm = new MainWindowViewModel();
        vm.ToggleSingleViewCommand.Execute(null);
        Assert.True(vm.IsSingleView);
        Assert.False(vm.IsDualView);
    }

    [Fact]
    public void ToggleSingleView_Twice_ReturnsToDualView()
    {
        var vm = new MainWindowViewModel();
        vm.ToggleSingleViewCommand.Execute(null);
        vm.ToggleSingleViewCommand.Execute(null);
        Assert.False(vm.IsSingleView);
        Assert.True(vm.IsDualView);
    }

    [Fact]
    public void SingleViewButtonLabel_ChangesWith_IsSingleView()
    {
        var vm = new MainWindowViewModel();

        vm.ToggleSingleViewCommand.Execute(null);
        Assert.Equal("⊞ Dual View", vm.SingleViewButtonLabel);

        vm.ToggleSingleViewCommand.Execute(null);
        Assert.Equal("⊟ Single View", vm.SingleViewButtonLabel);
    }

    [Fact]
    public void ToggleSingleView_RaisesPropertyChanged_ForRelatedProperties()
    {
        var vm = new MainWindowViewModel();
        var fired = new List<string?>();
        vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        vm.ToggleSingleViewCommand.Execute(null);

        Assert.Contains(nameof(vm.IsSingleView), fired);
        Assert.Contains(nameof(vm.IsDualView), fired);
        Assert.Contains(nameof(vm.SingleViewButtonLabel), fired);
    }

    [Fact]
    public void ToggleSingleViewCommand_IsAlwaysEnabled()
    {
        var vm = new MainWindowViewModel();
        Assert.True(vm.ToggleSingleViewCommand.CanExecute(null));
    }

    // ── CopySelected result / ShowAlertAsync ──────────────────────────

    [Fact]
    public async Task CopySelected_ShowsSuccessAlert_WhenAllFilesCopied()
    {
        WriteJpeg("ok.jpg", DateTime.Now);
        var destDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string? capturedTitle   = null;
        string? capturedMessage = null;

        var vm = MakeVm();
        vm.PickDestFolderAsync = () => Task.FromResult<string?>(destDir);
        vm.ShowAlertAsync      = (t, m) => { capturedTitle = t; capturedMessage = m; return Task.CompletedTask; };

        try
        {
            await vm.LoadFolderCommand.ExecuteAsync(null);
            vm.LeftPanel!.ToggleHeartCommand.Execute(null);
            await vm.CopySelectedCommand.ExecuteAsync(null);

            Assert.Equal("Copy Complete", capturedTitle);
            Assert.Contains("Copied:  1", capturedMessage);
        }
        finally
        {
            if (Directory.Exists(destDir)) Directory.Delete(destDir, recursive: true);
        }
    }

    [Fact]
    public async Task CopySelected_ShowsSkippedCount_WhenFileAlreadyExists()
    {
        var srcPath = WriteJpeg("dup.jpg", DateTime.Now);
        var destDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(destDir);
        // pre-place the file so it looks like a duplicate
        File.Copy(srcPath, Path.Combine(destDir, "dup.jpg"));
        string? capturedMessage = null;

        var vm = MakeVm();
        vm.PickDestFolderAsync = () => Task.FromResult<string?>(destDir);
        vm.ShowAlertAsync      = (_, m) => { capturedMessage = m; return Task.CompletedTask; };

        try
        {
            await vm.LoadFolderCommand.ExecuteAsync(null);
            vm.LeftPanel!.ToggleHeartCommand.Execute(null);
            await vm.CopySelectedCommand.ExecuteAsync(null);

            Assert.Contains("Skipped", capturedMessage);
        }
        finally
        {
            if (Directory.Exists(destDir)) Directory.Delete(destDir, recursive: true);
        }
    }

    [Fact]
    public async Task CopySelected_ShowsErrorAlert_WhenCopyFails()
    {
        WriteJpeg("fail.jpg", DateTime.Now);
        // Non-existent root path that can't be created — triggers Directory.CreateDirectory failure
        var destDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string? capturedTitle   = null;
        string? capturedMessage = null;

        var vm = MakeVm();
        vm.PickDestFolderAsync = () => Task.FromResult<string?>(destDir);
        vm.ShowAlertAsync      = (t, m) => { capturedTitle = t; capturedMessage = m; return Task.CompletedTask; };

        await vm.LoadFolderCommand.ExecuteAsync(null);
        vm.LeftPanel!.ToggleHeartCommand.Execute(null);

        // Make the source file unreadable by holding it open exclusively
        var srcPath = Path.Combine(_tempDir, "fail.jpg");
        using var lockHandle = new FileStream(srcPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        await vm.CopySelectedCommand.ExecuteAsync(null);

        Assert.Equal("Copy — Errors Occurred", capturedTitle);
        Assert.Contains("fail.jpg", capturedMessage);
        Assert.Contains("Failed:", capturedMessage);
    }

    [Fact]
    public async Task CopySelected_DoesNotThrow_WhenShowAlertAsyncIsNull()
    {
        WriteJpeg("x.jpg", DateTime.Now);
        var destDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        var vm = MakeVm();
        vm.PickDestFolderAsync = () => Task.FromResult<string?>(destDir);
        // ShowAlertAsync intentionally left null

        try
        {
            await vm.LoadFolderCommand.ExecuteAsync(null);
            vm.LeftPanel!.ToggleHeartCommand.Execute(null);

            var ex = await Record.ExceptionAsync(() => vm.CopySelectedCommand.ExecuteAsync(null));
            Assert.Null(ex);
        }
        finally
        {
            if (Directory.Exists(destDir)) Directory.Delete(destDir, recursive: true);
        }
    }

    // ── DeleteImageAsync ──────────────────────────────────────────────

    // Helper: loads the folder and wires ConfirmAsync to return the given answer.
    private async Task<MainWindowViewModel> MakeVmWithLoaded(bool confirmAnswer = true)
    {
        var vm = MakeVm();
        vm.ConfirmAsync   = (_, _) => Task.FromResult(confirmAnswer);
        vm.ShowAlertAsync = (_, _) => Task.CompletedTask;
        await vm.LoadFolderCommand.ExecuteAsync(null);
        return vm;
    }

    [Fact]
    public async Task Delete_Confirmed_RemovesFileFromDisk()
    {
        var path = WriteJpeg("del.jpg", DateTime.Now);
        var vm   = await MakeVmWithLoaded(confirmAnswer: true);

        await vm.DeleteImageAsync(vm.LeftPanel!.CurrentImage!);

        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Delete_Declined_LeavesFileOnDisk()
    {
        var path = WriteJpeg("keep.jpg", DateTime.Now);
        var vm   = await MakeVmWithLoaded(confirmAnswer: false);

        await vm.DeleteImageAsync(vm.LeftPanel!.CurrentImage!);

        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task Delete_Confirmed_RemovesImageFromList()
    {
        WriteJpeg("a.jpg", new DateTime(2024, 1, 1));
        WriteJpeg("b.jpg", new DateTime(2024, 2, 1));
        var vm = await MakeVmWithLoaded(confirmAnswer: true);
        var toDelete = vm.LeftPanel!.CurrentImage!;

        await vm.DeleteImageAsync(toDelete);

        Assert.DoesNotContain(vm.Images, i => i.FilePath == toDelete.FilePath);
    }

    [Fact]
    public async Task Delete_Confirmed_DecrementsImageCount()
    {
        WriteJpeg("a.jpg", new DateTime(2024, 1, 1));
        WriteJpeg("b.jpg", new DateTime(2024, 2, 1));
        var vm = await MakeVmWithLoaded(confirmAnswer: true);
        var before = vm.Images.Count;

        await vm.DeleteImageAsync(vm.LeftPanel!.CurrentImage!);

        Assert.Equal(before - 1, vm.Images.Count);
    }

    [Fact]
    public async Task Delete_Declined_DoesNotChangeImageCount()
    {
        WriteJpeg("a.jpg", new DateTime(2024, 1, 1));
        WriteJpeg("b.jpg", new DateTime(2024, 2, 1));
        var vm = await MakeVmWithLoaded(confirmAnswer: false);
        var before = vm.Images.Count;

        await vm.DeleteImageAsync(vm.LeftPanel!.CurrentImage!);

        Assert.Equal(before, vm.Images.Count);
    }

    [Fact]
    public async Task Delete_Confirmed_AlsoDeletesJsonSidecar()
    {
        WriteJpeg("photo.jpg", DateTime.Now);
        var sidecar = Path.Combine(_tempDir, "photo.json");
        File.WriteAllText(sidecar, "{}");
        var vm = await MakeVmWithLoaded(confirmAnswer: true);

        await vm.DeleteImageAsync(vm.LeftPanel!.CurrentImage!);

        Assert.False(File.Exists(sidecar));
    }

    [Fact]
    public async Task Delete_ConfirmMessage_IncludesSidecarName_WhenSidecarExists()
    {
        WriteJpeg("photo.jpg", DateTime.Now);
        File.WriteAllText(Path.Combine(_tempDir, "photo.json"), "{}");
        string? capturedMessage = null;
        var vm = MakeVm();
        vm.ConfirmAsync   = (_, m) => { capturedMessage = m; return Task.FromResult(false); };
        vm.ShowAlertAsync = (_, _) => Task.CompletedTask;
        await vm.LoadFolderCommand.ExecuteAsync(null);

        await vm.DeleteImageAsync(vm.LeftPanel!.CurrentImage!);

        Assert.Contains("photo.json", capturedMessage);
    }

    [Fact]
    public async Task Delete_ConfirmMessage_DoesNotMentionSidecar_WhenAbsent()
    {
        WriteJpeg("photo.jpg", DateTime.Now);
        string? capturedMessage = null;
        var vm = MakeVm();
        vm.ConfirmAsync   = (_, m) => { capturedMessage = m; return Task.FromResult(false); };
        vm.ShowAlertAsync = (_, _) => Task.CompletedTask;
        await vm.LoadFolderCommand.ExecuteAsync(null);

        await vm.DeleteImageAsync(vm.LeftPanel!.CurrentImage!);

        Assert.DoesNotContain(".json", capturedMessage);
        Assert.DoesNotContain(".txt",  capturedMessage);
    }

    [Fact]
    public async Task Delete_DoesNotDelete_WhenConfirmAsyncIsNull()
    {
        var path = WriteJpeg("safe.jpg", DateTime.Now);
        var vm   = MakeVm();
        // ConfirmAsync intentionally left null — should default to false (no delete)
        await vm.LoadFolderCommand.ExecuteAsync(null);

        await vm.DeleteImageAsync(vm.LeftPanel!.CurrentImage!);

        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task Delete_PanelsRebuildAfterDeletion()
    {
        WriteJpeg("a.jpg", new DateTime(2024, 1, 1));
        WriteJpeg("b.jpg", new DateTime(2024, 2, 1));
        var vm = await MakeVmWithLoaded(confirmAnswer: true);

        await vm.DeleteImageAsync(vm.LeftPanel!.CurrentImage!);

        // Panels must be non-null and pointing within bounds of the new (smaller) list
        Assert.NotNull(vm.LeftPanel);
        Assert.InRange(vm.LeftPanel!.CurrentIndex, 0, Math.Max(0, vm.Images.Count - 1));
    }

    // ── Filmstrip visibility ──────────────────────────────────────────

    [Fact]
    public void InitialState_IsFilmstripVisible_False()
    {
        var vm = new MainWindowViewModel();
        Assert.False(vm.IsFilmstripVisible);
    }

    [Fact]
    public void ToggleFilmstrip_MakesFilmstripVisible()
    {
        var vm = new MainWindowViewModel();
        vm.ToggleFilmstripCommand.Execute(null);
        Assert.True(vm.IsFilmstripVisible);
    }

    [Fact]
    public void ToggleFilmstrip_Twice_HidesFilmstrip()
    {
        var vm = new MainWindowViewModel();
        vm.ToggleFilmstripCommand.Execute(null);
        vm.ToggleFilmstripCommand.Execute(null);
        Assert.False(vm.IsFilmstripVisible);
    }

    // ── Filmstrip items ───────────────────────────────────────────────

    [Fact]
    public async Task LoadFolder_FilmstripItems_NotNull()
    {
        WriteJpeg("a.jpg", DateTime.Now);
        var vm = MakeVm();
        await vm.LoadFolderCommand.ExecuteAsync(null);
        Assert.NotNull(vm.FilmstripItems);
    }

    [Fact]
    public async Task LoadFolder_FilmstripItems_CountMatchesImages()
    {
        WriteJpeg("a.jpg", new DateTime(2024, 1, 1));
        WriteJpeg("b.jpg", new DateTime(2024, 2, 1));
        WriteJpeg("c.jpg", new DateTime(2024, 3, 1));
        var vm = MakeVm();
        await vm.LoadFolderCommand.ExecuteAsync(null);
        Assert.Equal(3, vm.FilmstripItems!.Count);
    }

    [Fact]
    public async Task LoadFolder_FilmstripItem0_IsCurrentImage()
    {
        WriteJpeg("a.jpg", new DateTime(2024, 1, 1));
        WriteJpeg("b.jpg", new DateTime(2024, 2, 1));
        var vm = MakeVm();
        await vm.LoadFolderCommand.ExecuteAsync(null);
        Assert.True(vm.FilmstripItems![0].IsCurrentImage);
        Assert.False(vm.FilmstripItems![1].IsCurrentImage);
    }

    [Fact]
    public async Task NavigatingActivePanel_UpdatesFilmstripCurrentImage()
    {
        WriteJpeg("a.jpg", new DateTime(2024, 1, 1));
        WriteJpeg("b.jpg", new DateTime(2024, 2, 1));
        var vm = MakeVm();
        await vm.LoadFolderCommand.ExecuteAsync(null);

        vm.LeftPanel!.CurrentIndex = 1;

        Assert.False(vm.FilmstripItems![0].IsCurrentImage);
        Assert.True(vm.FilmstripItems![1].IsCurrentImage);
    }

    [Fact]
    public async Task HeartToggle_SyncsFilmstripItemIsHearted()
    {
        WriteJpeg("a.jpg", DateTime.Now);
        var vm = MakeVm();
        await vm.LoadFolderCommand.ExecuteAsync(null);

        vm.LeftPanel!.ToggleHeartCommand.Execute(null);

        Assert.True(vm.FilmstripItems![0].IsHearted);
    }

    [Fact]
    public async Task HeartUntoggle_ClearsFilmstripItemIsHearted()
    {
        WriteJpeg("a.jpg", DateTime.Now);
        var vm = MakeVm();
        await vm.LoadFolderCommand.ExecuteAsync(null);

        vm.LeftPanel!.ToggleHeartCommand.Execute(null); // add
        vm.LeftPanel!.ToggleHeartCommand.Execute(null); // remove

        Assert.False(vm.FilmstripItems![0].IsHearted);
    }

    [Fact]
    public async Task Delete_Confirmed_RebuildsFilmstripItems()
    {
        WriteJpeg("a.jpg", new DateTime(2024, 1, 1));
        WriteJpeg("b.jpg", new DateTime(2024, 2, 1));
        var vm = await MakeVmWithLoaded(confirmAnswer: true);

        await vm.DeleteImageAsync(vm.LeftPanel!.CurrentImage!);

        Assert.NotNull(vm.FilmstripItems);
        Assert.Equal(vm.Images.Count, vm.FilmstripItems!.Count);
    }

    // ── Active panel ──────────────────────────────────────────────────

    [Fact]
    public async Task LoadFolder_ActivePanel_IsLeftPanel()
    {
        WriteJpeg("a.jpg", DateTime.Now);
        var vm = MakeVm();
        await vm.LoadFolderCommand.ExecuteAsync(null);
        Assert.Same(vm.LeftPanel, vm.ActivePanel);
    }

    [Fact]
    public async Task LoadFolder_IsLeftPanelActive_True()
    {
        WriteJpeg("a.jpg", DateTime.Now);
        var vm = MakeVm();
        await vm.LoadFolderCommand.ExecuteAsync(null);
        Assert.True(vm.IsLeftPanelActive);
        Assert.False(vm.IsRightPanelActive);
    }

    [Fact]
    public async Task SetActivePanel_RightPanel_UpdatesProperties()
    {
        WriteJpeg("a.jpg", new DateTime(2024, 1, 1));
        WriteJpeg("b.jpg", new DateTime(2024, 2, 1));
        var vm = MakeVm();
        await vm.LoadFolderCommand.ExecuteAsync(null);

        vm.SetActivePanel(vm.RightPanel);

        Assert.Same(vm.RightPanel, vm.ActivePanel);
        Assert.True(vm.IsRightPanelActive);
        Assert.False(vm.IsLeftPanelActive);
    }

    [Fact]
    public async Task SetActivePanel_RaisesPropertyChanged_ForIndicatorProperties()
    {
        WriteJpeg("a.jpg", new DateTime(2024, 1, 1));
        WriteJpeg("b.jpg", new DateTime(2024, 2, 1));
        var vm = MakeVm();
        await vm.LoadFolderCommand.ExecuteAsync(null);

        var fired = new List<string?>();
        vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        vm.SetActivePanel(vm.RightPanel);

        Assert.Contains(nameof(vm.ActivePanel), fired);
        Assert.Contains(nameof(vm.IsLeftPanelActive), fired);
        Assert.Contains(nameof(vm.IsRightPanelActive), fired);
    }

    [Fact]
    public async Task SetActivePanel_SamePanel_DoesNotFirePropertyChanged()
    {
        WriteJpeg("a.jpg", DateTime.Now);
        var vm = MakeVm();
        await vm.LoadFolderCommand.ExecuteAsync(null);

        var fired = new List<string?>();
        vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        vm.SetActivePanel(vm.LeftPanel); // already active

        Assert.DoesNotContain(nameof(vm.ActivePanel), fired);
    }

    // ── FilmstripItemClicked routing ──────────────────────────────────

    [Fact]
    public async Task FilmstripItemClicked_NavigatesLeftPanel_WhenLeftIsActive()
    {
        WriteJpeg("a.jpg", new DateTime(2024, 1, 1));
        WriteJpeg("b.jpg", new DateTime(2024, 2, 1));
        var vm = MakeVm();
        await vm.LoadFolderCommand.ExecuteAsync(null);

        var item = new ThumbnailItemViewModel(1, vm.Images[1].FilePath, vm.Images[1].FileName, false);
        vm.FilmstripItemClicked(item);

        Assert.Equal(1, vm.LeftPanel!.CurrentIndex);
    }

    [Fact]
    public async Task FilmstripItemClicked_NavigatesRightPanel_WhenRightIsActive()
    {
        WriteJpeg("a.jpg", new DateTime(2024, 1, 1));
        WriteJpeg("b.jpg", new DateTime(2024, 2, 1));
        var vm = MakeVm();
        await vm.LoadFolderCommand.ExecuteAsync(null);

        vm.SetActivePanel(vm.RightPanel);
        var item = new ThumbnailItemViewModel(0, vm.Images[0].FilePath, vm.Images[0].FileName, false);
        vm.FilmstripItemClicked(item);

        Assert.Equal(0, vm.RightPanel!.CurrentIndex);
        Assert.Equal(0, vm.LeftPanel!.CurrentIndex); // left panel untouched (starts at 0)
    }

    [Fact]
    public async Task FilmstripItemClicked_UpdatesFilmstripCurrentImage()
    {
        WriteJpeg("a.jpg", new DateTime(2024, 1, 1));
        WriteJpeg("b.jpg", new DateTime(2024, 2, 1));
        var vm = MakeVm();
        await vm.LoadFolderCommand.ExecuteAsync(null);

        var item = new ThumbnailItemViewModel(1, vm.Images[1].FilePath, vm.Images[1].FileName, false);
        vm.FilmstripItemClicked(item);

        Assert.False(vm.FilmstripItems![0].IsCurrentImage);
        Assert.True(vm.FilmstripItems![1].IsCurrentImage);
    }
}

