namespace PhotoComp.Services;

/// <summary>Summary of a move operation.</summary>
public sealed record MoveResult(
    int Moved,
    int Skipped,
    IReadOnlyList<string> MovedSourcePaths,
    IReadOnlyList<(string FileName, string Error)> Failures)
{
    public bool HasFailures => Failures.Count > 0;
}

public static class MoveService
{
    /// <summary>
    /// Moves each file in <paramref name="filePaths"/> to <paramref name="destinationFolder"/>.
    /// For each image, also moves any sidecar files found by <see cref="SidecarService"/>.
    /// Files that already exist at the destination are skipped (non-destructive).
    /// Uses copy-then-delete so a move failure never leaves the source deleted.
    /// Per-file errors are captured rather than thrown.
    /// </summary>
    public static MoveResult MoveSelected(IEnumerable<string> filePaths, string destinationFolder)
    {
        System.IO.Directory.CreateDirectory(destinationFolder);

        int moved = 0, skipped = 0;
        var movedSourcePaths = new List<string>();
        var failures = new List<(string FileName, string Error)>();

        foreach (var src in filePaths)
        {
            bool primaryMoved = TryMoveOne(src, destinationFolder, ref moved, ref skipped, failures);

            foreach (var sidecar in SidecarService.FindSidecars(src))
                TryMoveOne(sidecar, destinationFolder, ref moved, ref skipped, failures);

            if (primaryMoved)
                movedSourcePaths.Add(src);
        }

        return new MoveResult(moved, skipped, movedSourcePaths, failures);
    }

    /// <summary>Returns true if the primary file was moved (not skipped or failed).</summary>
    private static bool TryMoveOne(
        string src, string destinationFolder,
        ref int moved, ref int skipped,
        List<(string FileName, string Error)> failures)
    {
        var fileName = System.IO.Path.GetFileName(src);
        var destPath = System.IO.Path.Combine(destinationFolder, fileName);
        try
        {
            if (System.IO.File.Exists(destPath))
            {
                skipped++;
                return false;
            }

            // Copy first — source is untouched if this throws.
            System.IO.File.Copy(src, destPath);

            // Delete source only after successful copy.
            try
            {
                System.IO.File.Delete(src);
            }
            catch (Exception delEx)
            {
                // Copy succeeded but delete failed. Roll back the copy so the
                // destination doesn't end up with a duplicate.
                try { System.IO.File.Delete(destPath); } catch { }
                failures.Add((fileName, $"Copied but could not delete source: {delEx.Message}"));
                return false;
            }

            moved++;
            return true;
        }
        catch (Exception ex)
        {
            failures.Add((fileName, ex.Message));
            return false;
        }
    }
}
