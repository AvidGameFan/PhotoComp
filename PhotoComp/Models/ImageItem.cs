namespace PhotoComp.Models;

/// <summary>
/// Immutable record representing a single image in the loaded list.
/// </summary>
public sealed record ImageItem(
    string FilePath,
    string FileName,
    DateTime DateTaken,
    int Width,
    int Height);
