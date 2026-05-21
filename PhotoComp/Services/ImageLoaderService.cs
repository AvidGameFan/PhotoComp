using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Png;
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
            var (dateTaken, width, height, prompt, exifCaption) = ReadMetadata(files[i]);
                items[i] = new ImageItem(
                    FilePath: files[i],
                    FileName: System.IO.Path.GetFileName(files[i]),
                    DateTaken: dateTaken,
                    Width: width,
                    Height: height,
                    Prompt: prompt,
                    ExifCaption: exifCaption);
                return ValueTask.CompletedTask;
            });

        return items.OrderBy(i => i.DateTaken).ToList().AsReadOnly();
    }

    /// <inheritdoc cref="LoadImagesAsync"/>
    /// <remarks>Synchronous convenience wrapper used by unit tests.</remarks>
    public static IReadOnlyList<ImageItem> LoadImages(string folderPath)
        => LoadImagesAsync(folderPath).GetAwaiter().GetResult();

    private static (DateTime dateTaken, int width, int height, string? prompt, string? exifCaption) ReadMetadata(string filePath)
    {
        DateTime dateTaken = System.IO.File.GetLastWriteTime(filePath);
        int width = 0, height = 0;
        string? prompt = null;
        string? exifCaption = null;

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

            // SD prompt — PNG tEXt chunks
            // Keywords by app: "parameters" (A1111/Forge), "prompt" (Easy Diffusion, ComfyUI),
            //                  "invokeai_metadata" (InvokeAI), "sd-metadata" (Easy Diffusion older JSON)
            foreach (var dir in directories.OfType<PngDirectory>())
            {
                if (dir.GetObject(PngDirectory.TagTextualData) is
                        List<KeyValuePair<string, string>> pairs)
                {
                    foreach (var kv in pairs)
                    {
                        var value = ResolveSdPromptValue(kv.Key, kv.Value);
                        if (value != null) { prompt = value; break; }
                    }
                }
                if (prompt != null) break;
            }

            // SD prompt — JPEG EXIF UserComment (AUTOMATIC1111 JPEG output)
            if (prompt == null && exifSub != null)
            {
                var uc = exifSub.GetDescription(ExifSubIfdDirectory.TagUserComment);
                if (!string.IsNullOrWhiteSpace(uc) &&
                    (uc.Contains("Steps:") || uc.Contains("Negative prompt:")))
                {
                    prompt = uc;
                }
            }

            // Camera EXIF caption (shown in overlay when there is no SD prompt)
            exifCaption = BuildExifCaption(exifIfd0, exifSub);
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

        // Brute-force fallback: scan raw PNG bytes for tEXt/iTXt chunks.
        // Used when MetadataExtractor does not surface the chunk data (e.g. Easy Diffusion files).
        prompt ??= ExtractPromptFromPngBytes(filePath);

        return (dateTaken, width, height, prompt, exifCaption);
    }

    // ── Camera EXIF caption ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a one-line camera EXIF summary, e.g. "Sony ILCE-7M3 · ISO 400 · f/2.8 · 1/250 sec · 50 mm".
    /// Returns null when insufficient EXIF data is present (screenshots, SD-generated files, etc.).
    /// </summary>
    private static string? BuildExifCaption(ExifIfd0Directory? ifd0, ExifSubIfdDirectory? sub)
    {
        if (sub == null) return null;

        var parts = new List<string>();

        // Camera identification
        var make  = ifd0?.GetDescription(ExifIfd0Directory.TagMake)?.Trim();
        var model = ifd0?.GetDescription(ExifIfd0Directory.TagModel)?.Trim();
        if (!string.IsNullOrEmpty(model))
        {
            // Avoid "Sony Sony ILCE-7M3" when Make is already a prefix of Model
            var cameraName = (!string.IsNullOrEmpty(make) &&
                              !model.StartsWith(make, StringComparison.OrdinalIgnoreCase))
                ? $"{make} {model}"
                : model;
            parts.Add(cameraName);
        }

        // ISO
        var iso = sub.GetDescription(ExifSubIfdDirectory.TagIsoEquivalent);
        if (!string.IsNullOrWhiteSpace(iso)) parts.Add($"ISO {iso}");

        // Aperture — try f-number first, fall back to APEX aperture value
        var fNum = sub.GetDescription(ExifSubIfdDirectory.TagFNumber);
        if (string.IsNullOrWhiteSpace(fNum))
            fNum = sub.GetDescription(ExifSubIfdDirectory.TagAperture);
        if (!string.IsNullOrWhiteSpace(fNum)) parts.Add(fNum!);

        // Shutter speed
        var shutter = sub.GetDescription(ExifSubIfdDirectory.TagExposureTime);
        if (!string.IsNullOrWhiteSpace(shutter)) parts.Add(shutter!);

        // Focal length
        var focal = sub.GetDescription(ExifSubIfdDirectory.TagFocalLength);
        if (!string.IsNullOrWhiteSpace(focal)) parts.Add(focal!);

        // Only return a caption when there is at least one photographic setting —
        // a bare camera name with no exposure data suggests a non-photo file.
        bool hasExposureData = iso != null || fNum != null || shutter != null || focal != null;
        return hasExposureData && parts.Count > 0 ? string.Join(" · ", parts) : null;
    }

    // ── SD prompt keyword list (checked in MetadataExtractor and brute-force paths) ──────────

    // "parameters"      → AUTOMATIC1111, Forge, Stable Diffusion WebUI forks (one big text block)
    // "prompt"          → Easy Diffusion (individual tEXt chunk per field), some ComfyUI exports
    // "invokeai_metadata" → InvokeAI
    // "sd-metadata"     → Easy Diffusion older versions (JSON blob — "prompt" field extracted)
    // "Dream"           → sd-webui-dream-artist and older generators
    private static readonly string[] SdPromptKeywords =
        ["parameters", "prompt", "invokeai_metadata", "sd-metadata", "Dream"];

    /// <summary>
    /// Maps a PNG tEXt keyword + raw value to the displayable prompt string.
    /// Returns null if the keyword is not a recognised SD prompt field.
    /// </summary>
    private static string? ResolveSdPromptValue(string key, string value)
    {
        foreach (var kw in SdPromptKeywords)
        {
            if (!string.Equals(key, kw, StringComparison.OrdinalIgnoreCase))
                continue;

            // "sd-metadata" is a JSON blob — extract the nested "prompt" field.
            if (string.Equals(key, "sd-metadata", StringComparison.OrdinalIgnoreCase))
                value = ExtractJsonStringField(value, "prompt") ?? string.Empty;

            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        return null;
    }

    // ── Brute-force PNG byte scanner ─────────────────────────────────────────────────────────

    /// <summary>
    /// Fallback prompt extractor: reads the raw PNG bytes and searches for known tEXt / iTXt
    /// chunk keywords directly — the same approach used by Form1.cs for seeds.
    /// This handles files where MetadataExtractor does not surface the tEXt data.
    /// </summary>
    private static string? ExtractPromptFromPngBytes(string filePath)
    {
        if (!filePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            return null;

        byte[] data;
        try { data = File.ReadAllBytes(filePath); }
        catch { return null; }

        foreach (var keyword in SdPromptKeywords)
        {
            // Try tEXt (Latin-1) chunk
            var value = ReadPngTExtChunk(data, "tEXt", keyword, System.Text.Encoding.Latin1);
            // Try iTXt (UTF-8) chunk — same keyword, different chunk type and layout
            value ??= ReadPngITxtChunk(data, keyword);

            if (!string.IsNullOrWhiteSpace(value))
            {
                if (string.Equals(keyword, "sd-metadata", StringComparison.OrdinalIgnoreCase))
                    value = ExtractJsonStringField(value, "prompt");

                if (!string.IsNullOrWhiteSpace(value))
                    return value!.Trim();
            }
        }
        return null;
    }

    /// <summary>
    /// Scans <paramref name="data"/> for a PNG tEXt chunk with the given keyword
    /// and returns the decoded value, or null if not found.
    /// PNG tEXt layout: [4-byte data-length][4-byte "tEXt"][keyword][\0][value][4-byte CRC]
    /// </summary>
    private static string? ReadPngTExtChunk(byte[] data, string chunkType,
        string keyword, System.Text.Encoding encoding)
    {
        var pattern = System.Text.Encoding.ASCII.GetBytes(chunkType + keyword + "\0");
        int pos = IndexOfBytes(data, pattern);
        if (pos < 4) return null;

        int chunkLen = (data[pos - 4] << 24) | (data[pos - 3] << 16)
                     | (data[pos - 2] << 8)  |  data[pos - 1];
        int valueStart = pos + pattern.Length;
        int valueLen   = chunkLen - keyword.Length - 1;

        if (valueLen <= 0 || valueStart + valueLen > data.Length) return null;
        return encoding.GetString(data, valueStart, valueLen);
    }

    /// <summary>
    /// Scans for an uncompressed iTXt chunk (UTF-8, no language / translated keyword).
    /// iTXt layout: [4-byte length]["iTXt"][keyword][\0][0][0][""][\0][""][\0][UTF-8 text][CRC]
    /// </summary>
    private static string? ReadPngITxtChunk(byte[] data, string keyword)
    {
        var typeAndKeyword = System.Text.Encoding.ASCII.GetBytes("iTXt" + keyword + "\0");
        int pos = IndexOfBytes(data, typeAndKeyword);
        if (pos < 4) return null;

        int chunkLen   = (data[pos - 4] << 24) | (data[pos - 3] << 16)
                       | (data[pos - 2] << 8)  |  data[pos - 1];
        int afterKeywordNull = pos + typeAndKeyword.Length;

        // compression_flag(1) + compression_method(1) + language_tag + \0 + translated_keyword + \0
        // For the common uncompressed / no-lang case these are: 0, 0, \0, \0 (4 bytes total)
        // We scan forward to the second \0 after afterKeywordNull to find text start.
        int nullCount = 0;
        int textStart = afterKeywordNull;
        while (textStart < data.Length && nullCount < 2)
        {
            if (data[textStart] == 0) nullCount++;
            textStart++;
        }
        if (nullCount < 2) return null;

        int dataEnd    = pos - 4 + 4 + chunkLen; // start of chunk length + 4 (length field) + chunkLen
        int textLength = dataEnd - textStart;
        if (textLength <= 0 || textStart + textLength > data.Length) return null;
        return System.Text.Encoding.UTF8.GetString(data, textStart, textLength);
    }

    private static int IndexOfBytes(byte[] haystack, byte[] needle)
    {
        int limit = haystack.Length - needle.Length;
        for (int i = 0; i <= limit; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
                if (haystack[i + j] != needle[j]) { match = false; break; }
            if (match) return i;
        }
        return -1;
    }

    /// <summary>
    /// Extracts the string value of a top-level JSON field without a full JSON parser.
    /// Handles <c>"fieldName": "value"</c> — sufficient for well-formed SD metadata blobs.
    /// </summary>
    private static string? ExtractJsonStringField(string json, string fieldName)
    {
        int fieldPos = json.IndexOf($"\"{fieldName}\"", StringComparison.Ordinal);
        if (fieldPos < 0) return null;

        int colonPos = json.IndexOf(':', fieldPos);
        if (colonPos < 0) return null;

        int quoteOpen = json.IndexOf('"', colonPos + 1);
        if (quoteOpen < 0) return null;

        // Walk forward respecting \" escapes
        int start = quoteOpen + 1;
        int end   = start;
        while (end < json.Length)
        {
            if (json[end] == '\\') { end += 2; continue; }
            if (json[end] == '"')  break;
            end++;
        }
        return end > start ? json.Substring(start, end - start) : null;
    }
}
