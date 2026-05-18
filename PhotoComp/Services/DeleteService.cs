namespace PhotoComp.Services;

public sealed record DeleteResult(
    bool Deleted,
    IReadOnlyList<string> DeletedSidecars,
    string? Error);

public static class DeleteService
{
    /// <summary>
    /// Permanently deletes <paramref name="imagePath"/> from disk, then deletes any
    /// sidecar files found by <see cref="SidecarService"/> (best-effort).
    /// If the primary file cannot be deleted the operation stops immediately and
    /// no sidecars are touched. Sidecar failures do not affect the returned result.
    /// </summary>
    public static DeleteResult Delete(string imagePath)
    {
        // Locate sidecars before deleting so the path reference is still valid.
        var sidecars = SidecarService.FindSidecars(imagePath);

        try
        {
            System.IO.File.Delete(imagePath);
        }
        catch (Exception ex)
        {
            return new DeleteResult(false, [], ex.Message);
        }

        var deletedSidecars = new List<string>();
        foreach (var sidecar in sidecars)
        {
            try
            {
                System.IO.File.Delete(sidecar);
                deletedSidecars.Add(System.IO.Path.GetFileName(sidecar));
            }
            catch
            {
                // Best-effort: a sidecar failure does not fail the overall result.
            }
        }

        return new DeleteResult(true, deletedSidecars, null);
    }
}
