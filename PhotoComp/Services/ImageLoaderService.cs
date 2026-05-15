using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using PhotoComp.Models;
using Avalonia.Media.Imaging;

namespace PhotoComp.Services;

public static class ImageLoaderService
{
    private static readonly string[] SupportedExtensions =
        [".jpg", ".jpeg", ".png"];

    /// <summary>
    /// Scans <paramref name="folderPath"/> for supported images, reads EXIF metadata
    /// with up to <paramref name="maxDegreeOfParallelism"/> files processed concurrently,
    /// and returns a list sorted by date taken (ascending).
    /// </summary>
    public static async Task<IReadOnlyList<ImageItem>> LoadImagesAsync(
        string folderPath, int maxDegreeOfParallelism = 3)
    {
        var files = System.IO.Directory
            .EnumerateFiles(folderPath)
            .Where(f => SupportedExtensions.Contains(
                System.IO.Path.GetExtension(f).ToLowerInvariant()))
            .ToArray();

        var items = new ImageItem[files.Length];

        await Parallel.ForEachAsync(
            Enumerable.Range(0, files.Length),
            new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism },
            (i, _) =>
            {
                var (dateTaken, width, height) = ReadMetadata(files[i]);
                items[i] = new ImageItem(
                    FilePath: files[i],
                    FileName: System.IO.Path.GetFileName(files[i]),
                    DateTaken: dateTaken,
                    Width: width,
                    Height: height);
                return ValueTask.CompletedTask;
            });

        return items.OrderBy(i => i.DateTaken).ToList().AsReadOnly();
    }

    /// <inheritdoc cref="LoadImagesAsync"/>
    /// <remarks>Synchronous convenience wrapper used by unit tests.</remarks>
    public static IReadOnlyList<ImageItem> LoadImages(string folderPath)
        => LoadImagesAsync(folderPath).GetAwaiter().GetResult();

    private static (DateTime dateTaken, int width, int height) ReadMetadata(string filePath)
    {
        DateTime dateTaken = System.IO.File.GetLastWriteTime(filePath);
        int width = 0, height = 0;

        try
        {
            var directories = ImageMetadataReader.ReadMetadata(filePath);

            // EXIF date
            var exifSub = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            if (exifSub != null &&
                exifSub.TryGetDateTime(ExifSubIfdDirectory.TagDateTimeOriginal, out var exifDate))
            {
                dateTaken = exifDate;
            }

            // Pixel dimensions from JPEG EXIF
            var exifIfd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
            if (exifIfd0 != null &&
                exifIfd0.TryGetInt32(ExifDirectoryBase.TagImageWidth, out var exifW) &&
                exifIfd0.TryGetInt32(ExifDirectoryBase.TagImageHeight, out var exifH) &&
                exifW > 0 && exifH > 0)
            {
                width = exifW;
                height = exifH;
            }
        }
        catch
        {
            // MetadataExtractor failed — fall through to bitmap fallback below
        }

        // Fallback: load bitmap to get actual pixel dimensions
        if (width == 0 || height == 0)
        {
            try
            {
                using var bmp = new Bitmap(filePath);
                width = bmp.PixelSize.Width;
                height = bmp.PixelSize.Height;
            }
            catch
            {
                // Leave at 0 if even the bitmap can't be read
            }
        }

        return (dateTaken, width, height);
    }
}
