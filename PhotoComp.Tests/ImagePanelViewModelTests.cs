using PhotoComp.Models;
using PhotoComp.ViewModels;

namespace PhotoComp.Tests;

public class ImagePanelViewModelTests
{
    private static IReadOnlyList<ImageItem> MakeImages(int count) =>
        Enumerable.Range(0, count)
            .Select(i => new ImageItem(
                FilePath: $"/photos/img{i:D3}.jpg",
                FileName: $"img{i:D3}.jpg",
                DateTaken: new DateTime(2024, 1, 1).AddDays(i),
                Width: 1920,
                Height: 1080))
            .ToList()
            .AsReadOnly();

    private static (ImagePanelViewModel vm, HashSet<string> selected) Make(int count, int start = 0)
    {
        var images = MakeImages(count);
        var selected = new HashSet<string>();
        var zoom = new ZoomState();
        var vm = new ImagePanelViewModel(images, zoom, selected, start);
        return (vm, selected);
    }

    // ── Navigation ───────────────────────────────────────────────────

    [Fact]
    public void NavigateNext_AdvancesIndex()
    {
        var (vm, _) = Make(5, 0);
        vm.NavigateNextCommand.Execute(null);
        Assert.Equal(1, vm.CurrentIndex);
    }

    [Fact]
    public void NavigateNext_WrapsAtEnd()
    {
        var (vm, _) = Make(3, 2);
        vm.NavigateNextCommand.Execute(null);
        Assert.Equal(0, vm.CurrentIndex);
    }

    [Fact]
    public void NavigatePrevious_DecrementsIndex()
    {
        var (vm, _) = Make(5, 3);
        vm.NavigatePreviousCommand.Execute(null);
        Assert.Equal(2, vm.CurrentIndex);
    }

    [Fact]
    public void NavigatePrevious_WrapsAtStart()
    {
        var (vm, _) = Make(3, 0);
        vm.NavigatePreviousCommand.Execute(null);
        Assert.Equal(2, vm.CurrentIndex);
    }

    [Fact]
    public void Navigation_DoesNothing_WhenListEmpty()
    {
        var (vm, _) = Make(0);
        vm.NavigateNextCommand.Execute(null);
        vm.NavigatePreviousCommand.Execute(null);
        Assert.Equal(0, vm.CurrentIndex);
        Assert.Null(vm.CurrentImage);
    }

    // ── CurrentImage / PositionLabel ─────────────────────────────────

    [Fact]
    public void CurrentImage_ReturnsItemAtCurrentIndex()
    {
        var (vm, _) = Make(5, 2);
        Assert.Equal("/photos/img002.jpg", vm.CurrentImage?.FilePath);
    }

    [Fact]
    public void PositionLabel_ShowsCorrectNumbers()
    {
        var (vm, _) = Make(5, 1);
        Assert.Equal("2 / 5", vm.PositionLabel);
    }

    [Fact]
    public void PositionLabel_ShowsDash_WhenEmpty()
    {
        var (vm, _) = Make(0);
        Assert.Equal("—", vm.PositionLabel);
    }

    // ── Heart / selection ────────────────────────────────────────────

    [Fact]
    public void ToggleHeart_AddsToSelectedPaths()
    {
        var (vm, selected) = Make(3, 0);
        vm.ToggleHeartCommand.Execute(null);
        Assert.Contains("/photos/img000.jpg", selected);
    }

    [Fact]
    public void ToggleHeart_RemovesWhenAlreadySelected()
    {
        var (vm, selected) = Make(3, 0);
        vm.ToggleHeartCommand.Execute(null); // add
        vm.ToggleHeartCommand.Execute(null); // remove
        Assert.DoesNotContain("/photos/img000.jpg", selected);
    }

    [Fact]
    public void IsCurrentHearted_TrueAfterToggle()
    {
        var (vm, _) = Make(3, 0);
        Assert.False(vm.IsCurrentHearted);
        vm.ToggleHeartCommand.Execute(null);
        Assert.True(vm.IsCurrentHearted);
    }

    [Fact]
    public void IsCurrentHearted_FalseAfterDoubleToggle()
    {
        var (vm, _) = Make(3, 0);
        vm.ToggleHeartCommand.Execute(null);
        vm.ToggleHeartCommand.Execute(null);
        Assert.False(vm.IsCurrentHearted);
    }

    [Fact]
    public void HeartToggled_Event_Raised()
    {
        var (vm, _) = Make(3, 0);
        int raised = 0;
        vm.HeartToggled += (_, _) => raised++;
        vm.ToggleHeartCommand.Execute(null);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void ToggleHeart_DoesNothing_WhenEmpty()
    {
        var (vm, selected) = Make(0);
        vm.ToggleHeartCommand.Execute(null);
        Assert.Empty(selected);
    }

    // ── Shared selection ──────────────────────────────────────────────

    [Fact]
    public void TwoPanels_ShareSelectedPaths()
    {
        var images = MakeImages(5);
        var selected = new HashSet<string>();
        var zoom = new ZoomState();
        var left = new ImagePanelViewModel(images, zoom, selected, 0);
        var right = new ImagePanelViewModel(images, zoom, selected, 2);

        left.ToggleHeartCommand.Execute(null);

        // right panel should see the same selection set
        Assert.Single(selected);
        right.NavigatePreviousCommand.Execute(null); // move right to index 1
        right.NavigatePreviousCommand.Execute(null); // move right to index 0
        Assert.True(right.IsCurrentHearted);
    }

    // ── StartIndex clamping ───────────────────────────────────────────

    [Fact]
    public void StartIndex_ClampedToValidRange()
    {
        var (vm, _) = Make(3, 99);
        Assert.Equal(2, vm.CurrentIndex);
    }

    // ── InfoText (Phase 4) ────────────────────────────────────────────

    [Fact]
    public void InfoText_Empty_WhenNoImages()
    {
        var (vm, _) = Make(0);
        Assert.Equal(string.Empty, vm.InfoText);
    }

    [Fact]
    public void InfoText_ShowsDimensionsAndDate()
    {
        var images = new List<ImageItem>
        {
            new("/photos/a.jpg", "a.jpg", new DateTime(2024, 3, 15, 14, 32, 0), 1920, 1080)
        }.AsReadOnly();
        var vm = new ImagePanelViewModel(images, new ZoomState(), [], 0);

        Assert.Equal("1920×1080  |  2024-03-15 14:32:00", vm.InfoText);
    }

    [Fact]
    public void InfoText_UpdatesOnNavigation()
    {
        var images = new List<ImageItem>
        {
            new("/photos/a.jpg", "a.jpg", new DateTime(2024, 1, 1), 800, 600),
            new("/photos/b.jpg", "b.jpg", new DateTime(2024, 2, 1), 3840, 2160),
        }.AsReadOnly();
        var vm = new ImagePanelViewModel(images, new ZoomState(), [], 0);

        var first = vm.InfoText;
        vm.NavigateNextCommand.Execute(null);

        Assert.NotEqual(first, vm.InfoText);
        Assert.Contains("3840×2160", vm.InfoText);
    }

    // ── HeartGlyph (Phase 4) ──────────────────────────────────────────

    [Fact]
    public void HeartGlyph_IsOutlineHeart_WhenNotHearted()
    {
        var (vm, _) = Make(3, 0);
        Assert.Equal("\u2661", vm.HeartGlyph); // ♡
    }

    [Fact]
    public void HeartGlyph_IsFilledHeart_WhenHearted()
    {
        var (vm, _) = Make(3, 0);
        vm.ToggleHeartCommand.Execute(null);
        Assert.Equal("\u2665", vm.HeartGlyph); // ♥
    }

    [Fact]
    public void HeartGlyph_ReturnsToOutline_AfterDoubleToggle()
    {
        var (vm, _) = Make(3, 0);
        vm.ToggleHeartCommand.Execute(null);
        vm.ToggleHeartCommand.Execute(null);
        Assert.Equal("\u2661", vm.HeartGlyph);
    }

    [Fact]
    public void HeartGlyph_IsOutline_WhenEmpty()
    {
        var (vm, _) = Make(0);
        Assert.Equal("\u2661", vm.HeartGlyph);
    }

    // ── PropertyChanged notifications (Phase 4) ───────────────────────

    [Fact]
    public void Navigation_FiresPropertyChanged_ForInfoText()
    {
        var (vm, _) = Make(5, 0);
        var fired = new List<string?>();
        vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        vm.NavigateNextCommand.Execute(null);

        Assert.Contains(nameof(vm.InfoText), fired);
    }

    [Fact]
    public void Navigation_FiresPropertyChanged_ForHeartGlyph()
    {
        var (vm, _) = Make(5, 0);
        var fired = new List<string?>();
        vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        vm.NavigateNextCommand.Execute(null);

        Assert.Contains(nameof(vm.HeartGlyph), fired);
    }

    [Fact]
    public void ToggleHeart_FiresPropertyChanged_ForHeartGlyph()
    {
        var (vm, _) = Make(3, 0);
        var fired = new List<string?>();
        vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        vm.ToggleHeartCommand.Execute(null);

        Assert.Contains(nameof(vm.HeartGlyph), fired);
    }

    [Fact]
    public void ToggleHeart_FiresPropertyChanged_ForInfoText_NotRequired_ButIdempotent()
    {
        // InfoText itself does not change on heart toggle (it's navigation-driven),
        // but verifying that toggling does NOT raise a spurious InfoText notification
        // keeps the binding count low and avoids unnecessary redraws.
        var (vm, _) = Make(3, 0);
        var fired = new List<string?>();
        vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        vm.ToggleHeartCommand.Execute(null);

        Assert.DoesNotContain(nameof(vm.InfoText), fired);
    }

    // ── PromptText ────────────────────────────────────────────────────

    [Fact]
    public void PromptText_IsNull_WhenNoImages()
    {
        var (vm, _) = Make(0);
        Assert.Null(vm.PromptText);
    }

    [Fact]
    public void PromptText_IsNull_WhenNoPromptAndNoExifCaption()
    {
        // Default ImageItem constructor leaves both Prompt and ExifCaption null
        var (vm, _) = Make(3, 0);
        Assert.Null(vm.PromptText);
    }

    [Fact]
    public void PromptText_ReturnsSdPrompt_WhenSet()
    {
        var images = new List<ImageItem>
        {
            new("/photos/a.jpg", "a.jpg", DateTime.Now, 1920, 1080,
                Prompt: "a beautiful landscape, 8k, detailed")
        }.AsReadOnly();
        var vm = new ImagePanelViewModel(images, new ZoomState(), [], 0);

        Assert.Equal("a beautiful landscape, 8k, detailed", vm.PromptText);
    }

    [Fact]
    public void PromptText_ReturnsExifCaption_WhenNoSdPrompt()
    {
        var images = new List<ImageItem>
        {
            new("/photos/a.jpg", "a.jpg", DateTime.Now, 1920, 1080,
                Prompt: null, ExifCaption: "Sony ILCE-7M3 · ISO 400 · f/2.8 · 1/250 sec · 50 mm")
        }.AsReadOnly();
        var vm = new ImagePanelViewModel(images, new ZoomState(), [], 0);

        Assert.Equal("Sony ILCE-7M3 · ISO 400 · f/2.8 · 1/250 sec · 50 mm", vm.PromptText);
    }

    [Fact]
    public void PromptText_PrefersPrompt_OverExifCaption()
    {
        var images = new List<ImageItem>
        {
            new("/photos/a.jpg", "a.jpg", DateTime.Now, 1920, 1080,
                Prompt: "sd prompt text", ExifCaption: "Canon EOS R5 · ISO 100 · f/4.0 · 1/500 sec")
        }.AsReadOnly();
        var vm = new ImagePanelViewModel(images, new ZoomState(), [], 0);

        Assert.Equal("sd prompt text", vm.PromptText);
    }

    [Fact]
    public void PromptText_UpdatesOnNavigation()
    {
        var images = new List<ImageItem>
        {
            new("/photos/a.jpg", "a.jpg", DateTime.Now, 1920, 1080),
            new("/photos/b.jpg", "b.jpg", DateTime.Now, 1920, 1080,
                Prompt: "next image prompt"),
        }.AsReadOnly();
        var vm = new ImagePanelViewModel(images, new ZoomState(), [], 0);

        Assert.Null(vm.PromptText);
        vm.NavigateNextCommand.Execute(null);
        Assert.Equal("next image prompt", vm.PromptText);
    }

    [Fact]
    public void Navigation_FiresPropertyChanged_ForPromptText()
    {
        var (vm, _) = Make(5, 0);
        var fired = new List<string?>();
        vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        vm.NavigateNextCommand.Execute(null);

        Assert.Contains(nameof(vm.PromptText), fired);
    }

    // ── ShowPickerCommand ─────────────────────────────────────────────

    [Fact]
    public async Task ShowPickerCommand_DoesNothing_WhenShowPickerAsyncIsNull()
    {
        var (vm, _) = Make(5, 2);
        // ShowPickerAsync intentionally not set
        await vm.ShowPickerCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.CurrentIndex); // unchanged
    }

    [Fact]
    public async Task ShowPickerCommand_DoesNothing_WhenImagesEmpty()
    {
        var (vm, _) = Make(0);
        int callCount = 0;
        vm.ShowPickerAsync = _ => { callCount++; return Task.FromResult<int?>(0); };
        await vm.ShowPickerCommand.ExecuteAsync(null);
        Assert.Equal(0, callCount);
    }

    [Fact]
    public async Task ShowPickerCommand_PassesCurrentIndex_ToCallback()
    {
        var (vm, _) = Make(5, 3);
        int? receivedIndex = null;
        vm.ShowPickerAsync = idx => { receivedIndex = idx; return Task.FromResult<int?>(null); };
        await vm.ShowPickerCommand.ExecuteAsync(null);
        Assert.Equal(3, receivedIndex);
    }

    [Fact]
    public async Task ShowPickerCommand_UpdatesCurrentIndex_WhenValueReturned()
    {
        var (vm, _) = Make(5, 0);
        vm.ShowPickerAsync = _ => Task.FromResult<int?>(4);
        await vm.ShowPickerCommand.ExecuteAsync(null);
        Assert.Equal(4, vm.CurrentIndex);
    }

    [Fact]
    public async Task ShowPickerCommand_DoesNotUpdateCurrentIndex_WhenCallbackReturnsNull()
    {
        var (vm, _) = Make(5, 2);
        vm.ShowPickerAsync = _ => Task.FromResult<int?>(null);
        await vm.ShowPickerCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.CurrentIndex);
    }

    [Fact]
    public async Task ShowPickerCommand_ClampsReturnedValue_ToValidRange()
    {
        var (vm, _) = Make(5, 0);
        vm.ShowPickerAsync = _ => Task.FromResult<int?>(99); // out of range
        await vm.ShowPickerCommand.ExecuteAsync(null);
        Assert.Equal(4, vm.CurrentIndex); // clamped to last index
    }

    [Fact]
    public async Task ShowPickerCommand_ClampsNegativeReturnedValue()
    {
        var (vm, _) = Make(5, 3);
        vm.ShowPickerAsync = _ => Task.FromResult<int?>(-5); // negative
        await vm.ShowPickerCommand.ExecuteAsync(null);
        Assert.Equal(0, vm.CurrentIndex); // clamped to 0
    }
}
