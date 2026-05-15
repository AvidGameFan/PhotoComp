using PhotoComp.Services;

namespace PhotoComp.Tests;

public class ImageLoaderServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public ImageLoaderServiceTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private string WriteFakeJpeg(string name, DateTime lastWrite)
    {
        // Minimal valid JFIF header (no EXIF) — enough for a file to be picked up by the loader.
        // Dimensions will fall back to the Bitmap loader; since this isn't a real image,
        // width/height will be 0 — which is fine for these structural tests.
        var path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, [0xFF, 0xD8, 0xFF, 0xE0]);
        File.SetLastWriteTime(path, lastWrite);
        return path;
    }

    private string WriteEmptyFile(string name)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, []);
        return path;
    }

    // ── File filtering ────────────────────────────────────────────────

    [Fact]
    public void LoadImages_IncludesJpg()
    {
        WriteFakeJpeg("photo.jpg", DateTime.Now);
        var items = ImageLoaderService.LoadImages(_tempDir);
        Assert.Single(items);
    }

    [Fact]
    public void LoadImages_IncludesJpeg()
    {
        WriteFakeJpeg("photo.jpeg", DateTime.Now);
        var items = ImageLoaderService.LoadImages(_tempDir);
        Assert.Single(items);
    }

    [Fact]
    public void LoadImages_IncludesPng()
    {
        var path = Path.Combine(_tempDir, "photo.png");
        File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47]); // PNG magic bytes
        var items = ImageLoaderService.LoadImages(_tempDir);
        Assert.Single(items);
    }

    [Fact]
    public void LoadImages_ExcludesUnsupportedExtensions()
    {
        File.WriteAllText(Path.Combine(_tempDir, "readme.txt"), "hello");
        File.WriteAllText(Path.Combine(_tempDir, "photo.bmp"), "bmp");
        var items = ImageLoaderService.LoadImages(_tempDir);
        Assert.Empty(items);
    }

    [Fact]
    public void LoadImages_IsCaseInsensitiveOnExtension()
    {
        WriteFakeJpeg("PHOTO.JPG", DateTime.Now);
        var items = ImageLoaderService.LoadImages(_tempDir);
        Assert.Single(items);
    }

    // ── Date sorting ──────────────────────────────────────────────────

    [Fact]
    public void LoadImages_SortedByDateAscending()
    {
        var t1 = new DateTime(2024, 3, 1);
        var t2 = new DateTime(2024, 1, 1);
        var t3 = new DateTime(2024, 6, 1);

        WriteFakeJpeg("a.jpg", t1);
        WriteFakeJpeg("b.jpg", t2);
        WriteFakeJpeg("c.jpg", t3);

        var items = ImageLoaderService.LoadImages(_tempDir);

        Assert.Equal(3, items.Count);
        Assert.True(items[0].DateTaken <= items[1].DateTaken);
        Assert.True(items[1].DateTaken <= items[2].DateTaken);
    }

    // ── Metadata population ───────────────────────────────────────────

    [Fact]
    public void LoadImages_PopulatesFilePath()
    {
        var path = WriteFakeJpeg("img.jpg", DateTime.Now);
        var items = ImageLoaderService.LoadImages(_tempDir);
        Assert.Equal(path, items[0].FilePath);
    }

    [Fact]
    public void LoadImages_PopulatesFileName()
    {
        WriteFakeJpeg("my_photo.jpg", DateTime.Now);
        var items = ImageLoaderService.LoadImages(_tempDir);
        Assert.Equal("my_photo.jpg", items[0].FileName);
    }

    [Fact]
    public void LoadImages_FallsBackToFileWriteTime_WhenNoExif()
    {
        var expected = new DateTime(2023, 7, 15, 10, 0, 0);
        WriteFakeJpeg("noexif.jpg", expected);
        var items = ImageLoaderService.LoadImages(_tempDir);
        // Allow 1-second tolerance for filesystem rounding
        Assert.Equal(expected, items[0].DateTaken, TimeSpan.FromSeconds(1));
    }

    // ── Empty folder ──────────────────────────────────────────────────

    [Fact]
    public void LoadImages_ReturnsEmpty_WhenFolderHasNoImages()
    {
        var items = ImageLoaderService.LoadImages(_tempDir);
        Assert.Empty(items);
    }

    // ── Resilience ────────────────────────────────────────────────────

    [Fact]
    public void LoadImages_DoesNotThrow_OnCorruptFile()
    {
        WriteEmptyFile("corrupt.jpg");
        var ex = Record.Exception(() => ImageLoaderService.LoadImages(_tempDir));
        Assert.Null(ex);
    }
}
