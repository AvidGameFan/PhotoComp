namespace PhotoComp.Services;

/// <summary>Locates sidecar files that accompany an image on disk.</summary>
public static class SidecarService
{
    public static readonly IReadOnlyList<string> Extensions = [".json", ".txt"];

    /// <summary>
    /// Returns the paths of sidecar files that currently exist alongside
    /// <paramref name="imagePath"/>. A sidecar shares the same directory and
    /// base name with a .json or .txt extension.
    /// </summary>
    public static IReadOnlyList<string> FindSidecars(string imagePath)
    {
        var dir  = System.IO.Path.GetDirectoryName(imagePath) ?? string.Empty;
        var stem = System.IO.Path.GetFileNameWithoutExtension(imagePath);
        var found = new List<string>();
        foreach (var ext in Extensions)
        {
            var candidate = System.IO.Path.Combine(dir, stem + ext);
            if (System.IO.File.Exists(candidate))
                found.Add(candidate);
        }
        return found;
    }
}
