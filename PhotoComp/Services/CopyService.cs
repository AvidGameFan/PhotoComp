namespace PhotoComp.Services;

/// <summary>Summary of a copy operation.</summary>
public sealed record CopyResult(
    int Copied,
    int Skipped,
    IReadOnlyList<(string FileName, string Error)> Failures)
{
    public bool HasFailures => Failures.Count > 0;
}

public static class CopyService
{
    /// <summary>
    /// Copies each file in <paramref name="filePaths"/> to <paramref name="destinationFolder"/>.
    /// For each image, also copies any sidecar files found by <see cref="SidecarService"/>.
    /// Files that already exist at the destination are skipped (non-destructive).
    /// Per-file errors are captured rather than thrown, so a single bad file never
    /// aborts the rest of the operation.
    /// </summary>
    public static CopyResult CopySelected(IEnumerable<string> filePaths, string destinationFolder)
    {
        System.IO.Directory.CreateDirectory(destinationFolder);

        int copied = 0, skipped = 0;
        var failures = new List<(string FileName, string Error)>();

        foreach (var src in filePaths)
        {
            TryCopyOne(src, destinationFolder, ref copied, ref skipped, failures);

            foreach (var sidecar in SidecarService.FindSidecars(src))
                TryCopyOne(sidecar, destinationFolder, ref copied, ref skipped, failures);
        }

        return new CopyResult(copied, skipped, failures);
    }

    private static void TryCopyOne(
        string src, string destinationFolder,
        ref int copied, ref int skipped,
        List<(string FileName, string Error)> failures)
    {
        var fileName = System.IO.Path.GetFileName(src);
        var destPath = System.IO.Path.Combine(destinationFolder, fileName);
        try
        {
            if (System.IO.File.Exists(destPath))
            {
                skipped++;
            }
            else
            {
                System.IO.File.Copy(src, destPath);
                copied++;
            }
        }
        catch (Exception ex)
        {
            failures.Add((fileName, ex.Message));
        }
    }
}
