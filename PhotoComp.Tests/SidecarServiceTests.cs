using PhotoComp.Services;

namespace PhotoComp.Tests;

public class SidecarServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public SidecarServiceTests() => Directory.CreateDirectory(_tempDir);
    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private string WriteFile(string name, string content = "")
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    // ── Extensions list ───────────────────────────────────────────────

    [Fact]
    public void Extensions_ContainsXmp()
    {
        Assert.Contains(".xmp", SidecarService.Extensions);
    }

    [Fact]
    public void Extensions_ContainsJsonAndTxt()
    {
        Assert.Contains(".json", SidecarService.Extensions);
        Assert.Contains(".txt",  SidecarService.Extensions);
    }

    [Fact]
    public void Extensions_ContainsCanonRawFormats()
    {
        Assert.Contains(".cr2", SidecarService.Extensions);
        Assert.Contains(".cr3", SidecarService.Extensions);
    }

    [Fact]
    public void Extensions_ContainsNikonRawFormats()
    {
        Assert.Contains(".nef", SidecarService.Extensions);
        Assert.Contains(".nrw", SidecarService.Extensions);
    }

    [Fact]
    public void Extensions_ContainsSonyArw()
    {
        Assert.Contains(".arw", SidecarService.Extensions);
    }

    [Fact]
    public void Extensions_ContainsFujifilmRaf()
    {
        Assert.Contains(".raf", SidecarService.Extensions);
    }

    [Fact]
    public void Extensions_ContainsDng()
    {
        Assert.Contains(".dng", SidecarService.Extensions);
    }

    // ── FindSidecars — nothing present ───────────────────────────────

    [Fact]
    public void FindSidecars_ReturnsEmpty_WhenNoSidecarsExist()
    {
        WriteFile("photo.jpg");
        var result = SidecarService.FindSidecars(Path.Combine(_tempDir, "photo.jpg"));
        Assert.Empty(result);
    }

    [Fact]
    public void FindSidecars_ReturnsEmpty_WhenImageFileDoesNotExist()
    {
        // No files in _tempDir at all — should not throw, just return empty
        var result = SidecarService.FindSidecars(Path.Combine(_tempDir, "missing.jpg"));
        Assert.Empty(result);
    }

    // ── FindSidecars — individual extensions ─────────────────────────

    [Fact]
    public void FindSidecars_FindsJsonSidecar()
    {
        WriteFile("photo.jpg");
        var sidecar = WriteFile("photo.json", "{}");
        var result = SidecarService.FindSidecars(Path.Combine(_tempDir, "photo.jpg"));
        Assert.Single(result);
        Assert.Equal(sidecar, result[0]);
    }

    [Fact]
    public void FindSidecars_FindsTxtSidecar()
    {
        WriteFile("photo.jpg");
        var sidecar = WriteFile("photo.txt", "caption");
        var result = SidecarService.FindSidecars(Path.Combine(_tempDir, "photo.jpg"));
        Assert.Single(result);
        Assert.Equal(sidecar, result[0]);
    }

    [Fact]
    public void FindSidecars_FindsXmpSidecar()
    {
        WriteFile("photo.jpg");
        var sidecar = WriteFile("photo.xmp", "<x:xmpmeta/>");
        var result = SidecarService.FindSidecars(Path.Combine(_tempDir, "photo.jpg"));
        Assert.Single(result);
        Assert.Equal(sidecar, result[0]);
    }

    [Fact]
    public void FindSidecars_FindsNefRawSidecar()
    {
        WriteFile("photo.jpg");
        var sidecar = WriteFile("photo.nef");
        var result = SidecarService.FindSidecars(Path.Combine(_tempDir, "photo.jpg"));
        Assert.Contains(sidecar, result);
    }

    [Fact]
    public void FindSidecars_FindsArwRawSidecar()
    {
        WriteFile("photo.jpg");
        var sidecar = WriteFile("photo.arw");
        var result = SidecarService.FindSidecars(Path.Combine(_tempDir, "photo.jpg"));
        Assert.Contains(sidecar, result);
    }

    // ── FindSidecars — multiple / filtering ──────────────────────────

    [Fact]
    public void FindSidecars_FindsMultipleSidecars()
    {
        WriteFile("photo.jpg");
        WriteFile("photo.json", "{}");
        WriteFile("photo.txt",  "caption");
        WriteFile("photo.xmp",  "<x:xmpmeta/>");
        var result = SidecarService.FindSidecars(Path.Combine(_tempDir, "photo.jpg"));
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void FindSidecars_DoesNotReturn_DifferentStemFile()
    {
        WriteFile("photo.jpg");
        WriteFile("other.json", "{}");
        var result = SidecarService.FindSidecars(Path.Combine(_tempDir, "photo.jpg"));
        Assert.Empty(result);
    }

    [Fact]
    public void FindSidecars_DoesNotReturn_SiblingImageFile()
    {
        // A .jpg file with a different stem must not appear in the sidecar list
        WriteFile("photo.jpg");
        WriteFile("photo2.jpg");
        var result = SidecarService.FindSidecars(Path.Combine(_tempDir, "photo.jpg"));
        Assert.Empty(result);
    }

    [Fact]
    public void FindSidecars_ResultContainsFullPaths()
    {
        WriteFile("shot.jpg");
        WriteFile("shot.json", "{}");
        var result = SidecarService.FindSidecars(Path.Combine(_tempDir, "shot.jpg"));
        Assert.All(result, p => Assert.True(Path.IsPathRooted(p)));
    }

    [Fact]
    public void FindSidecars_DoesNotCountSameFileTwice()
    {
        // Ensures no duplicate entries in the returned list
        WriteFile("photo.jpg");
        WriteFile("photo.json", "{}");
        var result = SidecarService.FindSidecars(Path.Combine(_tempDir, "photo.jpg"));
        Assert.Equal(result.Count, result.Distinct().Count());
    }
}
