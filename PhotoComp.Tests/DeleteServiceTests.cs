using PhotoComp.Services;

namespace PhotoComp.Tests;

public class DeleteServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public DeleteServiceTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private string WriteFile(string name, string content = "data")
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    // ── Success cases ─────────────────────────────────────────────────

    [Fact]
    public void Delete_RemovesImageFile()
    {
        var path = WriteFile("photo.jpg");
        DeleteService.Delete(path);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Delete_ReturnsDeletedTrue_And_NullError_OnSuccess()
    {
        var path   = WriteFile("photo.jpg");
        var result = DeleteService.Delete(path);
        Assert.True(result.Deleted);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Delete_ReturnsEmptyDeletedSidecars_WhenNoSidecarExists()
    {
        var path   = WriteFile("photo.jpg");
        var result = DeleteService.Delete(path);
        Assert.Empty(result.DeletedSidecars);
    }

    [Fact]
    public void Delete_AlsoRemovesJsonSidecar_AndReportsIt()
    {
        var path    = WriteFile("photo.jpg");
        var sidecar = WriteFile("photo.json", "{}");

        var result = DeleteService.Delete(path);

        Assert.True(result.Deleted);
        Assert.False(File.Exists(sidecar));
        Assert.Contains("photo.json", result.DeletedSidecars);
    }

    [Fact]
    public void Delete_AlsoRemovesTxtSidecar_AndReportsIt()
    {
        var path    = WriteFile("photo.jpg");
        var sidecar = WriteFile("photo.txt", "caption");

        var result = DeleteService.Delete(path);

        Assert.True(result.Deleted);
        Assert.False(File.Exists(sidecar));
        Assert.Contains("photo.txt", result.DeletedSidecars);
    }

    [Fact]
    public void Delete_RemovesBothSidecars_WhenBothExist()
    {
        var path = WriteFile("photo.jpg");
        WriteFile("photo.json", "{}");
        WriteFile("photo.txt", "caption");

        var result = DeleteService.Delete(path);

        Assert.Equal(2, result.DeletedSidecars.Count);
    }

    // ── Failure cases ─────────────────────────────────────────────────

    [Fact]
    public void Delete_ReturnsFalse_WhenFileLocked()
    {
        var path = WriteFile("locked.jpg");
        using var lockHandle = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var result = DeleteService.Delete(path);

        Assert.False(result.Deleted);
        Assert.NotNull(result.Error);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Delete_DoesNotDeleteSidecar_WhenImageDeleteFails()
    {
        var path    = WriteFile("locked.jpg");
        var sidecar = WriteFile("locked.json", "{}");
        using var lockHandle = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var result = DeleteService.Delete(path);

        Assert.False(result.Deleted);
        Assert.True(File.Exists(sidecar)); // sidecar must be untouched when image fails
        Assert.Empty(result.DeletedSidecars);
    }

    [Fact]
    public void Delete_NonExistentFile_StillReturnsDeleted()
    {
        // File.Delete on a non-existent path is a no-op in .NET — not an error.
        var path   = Path.Combine(_tempDir, "ghost.jpg");
        var result = DeleteService.Delete(path);
        Assert.True(result.Deleted);
    }
}
