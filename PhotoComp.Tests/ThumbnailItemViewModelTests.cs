using PhotoComp.ViewModels;

namespace PhotoComp.Tests;

public class ThumbnailItemViewModelTests
{
    // ── Constructor properties ────────────────────────────────────────

    [Fact]
    public void Constructor_SetsIndex()
    {
        var vm = new ThumbnailItemViewModel(3, "/photos/img.jpg", "img.jpg", false);
        Assert.Equal(3, vm.Index);
    }

    [Fact]
    public void Constructor_SetsFilePath()
    {
        var vm = new ThumbnailItemViewModel(0, "/photos/img.jpg", "img.jpg", false);
        Assert.Equal("/photos/img.jpg", vm.FilePath);
    }

    [Fact]
    public void Constructor_SetsFileName()
    {
        var vm = new ThumbnailItemViewModel(0, "/photos/img.jpg", "img.jpg", false);
        Assert.Equal("img.jpg", vm.FileName);
    }

    [Fact]
    public void Constructor_SetsIsCurrentImage_True()
    {
        var vm = new ThumbnailItemViewModel(2, "/photos/img.jpg", "img.jpg", true);
        Assert.True(vm.IsCurrentImage);
    }

    [Fact]
    public void Constructor_SetsIsCurrentImage_False()
    {
        var vm = new ThumbnailItemViewModel(0, "/photos/img.jpg", "img.jpg", false);
        Assert.False(vm.IsCurrentImage);
    }

    [Fact]
    public void Constructor_ThumbnailIsNull_Initially()
    {
        var vm = new ThumbnailItemViewModel(0, "/photos/img.jpg", "img.jpg", false);
        Assert.Null(vm.Thumbnail);
    }

    // ── Index zero is valid ───────────────────────────────────────────

    [Fact]
    public void Constructor_IndexZero_IsValid()
    {
        var vm = new ThumbnailItemViewModel(0, "/a.jpg", "a.jpg", false);
        Assert.Equal(0, vm.Index);
    }

    // ── LoadAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_DoesNotThrow_WithInvalidPath()
    {
        var vm = new ThumbnailItemViewModel(0, @"C:\nonexistent\no_such_file_xyz.jpg", "no_such_file_xyz.jpg", false);
        var ex = await Record.ExceptionAsync(() => vm.LoadAsync());
        Assert.Null(ex);
    }

    [Fact]
    public async Task LoadAsync_ThumbnailRemainsNull_WhenPathIsInvalid()
    {
        var vm = new ThumbnailItemViewModel(0, @"C:\nonexistent\no_such_file_xyz.jpg", "no_such_file_xyz.jpg", false);
        await vm.LoadAsync();
        Assert.Null(vm.Thumbnail);
    }

    [Fact]
    public async Task LoadAsync_CancelledToken_DoesNotThrow()
    {
        var vm = new ThumbnailItemViewModel(0, @"C:\nonexistent\no_such_file_xyz.jpg", "no_such_file_xyz.jpg", false);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var ex = await Record.ExceptionAsync(() => vm.LoadAsync(cts.Token));
        Assert.Null(ex);
    }

    [Fact]
    public async Task LoadAsync_CancelledToken_ThumbnailRemainsNull()
    {
        var vm = new ThumbnailItemViewModel(0, @"C:\nonexistent\no_such_file_xyz.jpg", "no_such_file_xyz.jpg", false);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await vm.LoadAsync(cts.Token);
        Assert.Null(vm.Thumbnail);
    }

    [Fact]
    public async Task LoadAsync_RaisesPropertyChanged_WhenThumbnailChanges()
    {
        // Use an invalid path so the bitmap fails gracefully (returns null).
        // The property-changed event fires for any assignment to Thumbnail,
        // including null → null when the load fails.
        // To observe the notification we need a path that at least attempts loading.
        var vm = new ThumbnailItemViewModel(0, @"C:\nonexistent\no_such_file_xyz.jpg", "no_such_file_xyz.jpg", false);
        var fired = new List<string?>();
        vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        await vm.LoadAsync();

        // The setter is always called (even when the bitmap is null),
        // so PropertyChanged must fire exactly once for Thumbnail.
        Assert.Contains(nameof(vm.Thumbnail), fired);
    }
}
