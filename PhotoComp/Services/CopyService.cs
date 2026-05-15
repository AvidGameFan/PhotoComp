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
    private static readonly string[] SidecarExtensions = [".json", ".txt"];

    /// <summary>
    /// Copies each file in <paramref name="filePaths"/> to <paramref name="destinationFolder"/>.
    /// For each image, looks for a sidecar file with the same base name and a .json or .txt
    /// extension, and copies it too if found.
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

            // Sidecar: same directory, same base name, .json or .txt
            var dir  = System.IO.Path.GetDirectoryName(src) ?? string.Empty;
            var stem = System.IO.Path.GetFileNameWithoutExtension(src);
            foreach (var ext in SidecarExtensions)
            {
                var sidecar = System.IO.Path.Combine(dir, stem + ext);
                if (System.IO.File.Exists(sidecar))
                    TryCopyOne(sidecar, destinationFolder, ref copied, ref skipped, failures);
            }
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
