namespace PhotoComp.Services;

/// <summary>Locates sidecar files that accompany an image on disk.</summary>
public static class SidecarService
{
    public static readonly IReadOnlyList<string> Extensions =
    [
        // Metadata sidecars
        ".xmp",   // Adobe XMP sidecar (written by Lightroom, Bridge, darktable, etc.)
        ".json",
        ".txt",

        // Canon
        ".cr2",   // Canon DSLRs (pre-2018)
        ".cr3",   // Canon DSLRs / mirrorless (2018+)

        // Nikon
        ".nef",   // Nikon DSLRs / mirrorless
        ".nrw",   // Nikon compact cameras

        // Sony
        ".arw",   // Sony Alpha

        // Fujifilm
        ".raf",   // Fujifilm X / GFX series

        // Panasonic / Leica
        ".rw2",   // Panasonic Lumix
        ".raw",   // Leica, Panasonic (some models)
        ".rwl",   // Leica

        // Olympus / OM System
        ".orf",   // Olympus / OM System

        // Pentax / Ricoh
        ".pef",   // Pentax
        ".dng",   // Adobe DNG (Leica, DJI, Ricoh, Samsung, converted files)

        // Hasselblad
        ".3fr",   // Hasselblad
        ".fff",   // Hasselblad

        // Phase One / Mamiya
        ".iiq",   // Phase One
        ".mef",   // Mamiya

        // Sigma
        ".x3f",   // Sigma Foveon

        // Minolta / Konica Minolta
        ".mrw",   // Minolta / Konica Minolta

        // Samsung
        ".srw",   // Samsung NX

        // Kodak
        ".kdc",   // Kodak
        ".dcr",   // Kodak

        // Epson
        ".erf",   // Epson
    ];

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
