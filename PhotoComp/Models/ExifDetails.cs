namespace PhotoComp.Models;

/// <summary>A single label/value row shown in the EXIF detail overlay.</summary>
public sealed record ExifRow(string Label, string Value);

/// <summary>Structured EXIF/metadata extracted from an image file.</summary>
public sealed record ExifDetails(
    string? CameraMake,
    string? CameraModel,
    string? LensMake,
    string? LensModel,
    string? Iso,
    string? Aperture,
    string? ShutterSpeed,
    string? FocalLength,
    string? FocalLength35mm,
    string? ExposureBias,
    string? ExposureProgram,
    string? MeteringMode,
    string? Flash,
    string? WhiteBalance);
