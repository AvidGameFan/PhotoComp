using System.Text.Json;
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
    /// concurrently, and returns a list sorted by date taken (ascending).
    ///
    /// DOP defaults to 2× logical CPU count: metadata reading is I/O-bound so more
    /// concurrent operations keep the disk pipeline full without excess context switching.
    /// </summary>
    public static async Task<IReadOnlyList<ImageItem>> LoadImagesAsync(
        string folderPath, int maxDegreeOfParallelism = -1)
    {
        if (maxDegreeOfParallelism <= 0)
            maxDegreeOfParallelism = Environment.ProcessorCount * 2;

        var files = System.IO.Directory
            .EnumerateFiles(folderPath)
            .Where(f => SupportedExtensions.Contains(
                System.IO.Path.GetExtension(f).ToLowerInvariant()))
            .ToArray();

        var items = new ImageItem[files.Length];

        await Parallel.ForEachAsync(
            Enumerable.Range(0, files.Length),
            new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism },
            async (i, ct) =>
            {
                var (dateTaken, width, height, prompt, exifCaption, exifDetails, aiDetails) =
                    await ReadMetadataAsync(files[i]).ConfigureAwait(false);

                items[i] = new ImageItem(
                    FilePath:    files[i],
                    FileName:    System.IO.Path.GetFileName(files[i]),
                    DateTaken:   dateTaken,
                    Width:       width,
                    Height:      height,
                    Prompt:      prompt,
                    ExifCaption: exifCaption,
                    ExifDetails: exifDetails,
                    AiDetails:   aiDetails);
            });

        return items.OrderBy(i => i.DateTaken).ToList().AsReadOnly();
    }

    /// <inheritdoc cref="LoadImagesAsync"/>
    /// <remarks>Synchronous convenience wrapper used by unit tests.</remarks>
    public static IReadOnlyList<ImageItem> LoadImages(string folderPath)
        => LoadImagesAsync(folderPath).GetAwaiter().GetResult();

    private static async Task<(DateTime dateTaken, int width, int height, string? prompt, string? exifCaption, ExifDetails? exifDetails, AiDetails? aiDetails)>
        ReadMetadataAsync(string filePath)
    {
        // ImageMetadataReader has no async API — offload it so the calling thread is freed
        // during the synchronous disk read rather than blocking a pool thread.
        var (dateTaken, width, height, prompt, exifCaption, exifDetails, aiDetails) =
            await Task.Run(() => ReadMetadata(filePath)).ConfigureAwait(false);

        // If the brute-force PNG fallback is needed, run it with true async I/O.
        if (prompt == null)
            prompt = await ExtractPromptFromPngBytesAsync(filePath).ConfigureAwait(false);

        return (dateTaken, width, height, prompt, exifCaption, exifDetails, aiDetails);
    }

    private static (DateTime dateTaken, int width, int height, string? prompt, string? exifCaption, ExifDetails? exifDetails, AiDetails? aiDetails) ReadMetadata(string filePath)
    {
        DateTime dateTaken = System.IO.File.GetLastWriteTime(filePath);
        int width = 0, height = 0;
        string? prompt = null;
        string? exifCaption = null;
        ExifDetails? exifDetails = null;
        AiDetails? aiDetails = null;

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

            // SD prompt + AI fields — PNG tEXt chunks.
            // MetadataExtractor 2.9.0 creates one PngDirectory per tEXt chunk; each stores
            // TagTextualData as List<MetadataExtractor.KeyValuePair> (tEXt) or
            // MetadataExtractor.KeyValuePair[] (iTXt/zTXt). Both implement IEnumerable.
            // Keywords by app: "parameters" (A1111/Forge), "prompt" (Easy Diffusion, ComfyUI),
            //                  "invokeai_metadata" (InvokeAI), "sd-metadata" (Easy Diffusion older JSON)
            var pngFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dir in directories.OfType<PngDirectory>())
            {
                if (dir.GetObject(PngDirectory.TagTextualData) is
                        IEnumerable<MetadataExtractor.KeyValuePair> pairs)
                {
                    foreach (var kv in pairs)
                        pngFields.TryAdd(kv.Key, kv.Value.ToString());
                }
            }

            // If this is a ComfyUI workflow JSON, normalise its graph into flat fields.
            ApplyComfyUiFields(pngFields);

            foreach (var keyword in SdPromptKeywords)
            {
                if (pngFields.TryGetValue(keyword, out var raw))
                {
                    var resolved = ResolveSdPromptValue(keyword, raw);
                    if (resolved != null) { prompt = resolved; break; }
                }
            }

            aiDetails = BuildAiDetails(pngFields);

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
            exifDetails = BuildExifDetails(exifIfd0, exifSub);
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

        // Brute-force PNG chunk fallback is handled by ReadMetadataAsync to allow true async I/O.
        // When called synchronously (unit tests via LoadImages), run it inline.
        prompt ??= ExtractPromptFromPngBytes(filePath);

        return (dateTaken, width, height, prompt, exifCaption, exifDetails, aiDetails);
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

    /// <summary>
    /// Builds structured EXIF details for the expandable info overlay.
    /// Returns null when no meaningful EXIF is available.
    /// </summary>
    private static ExifDetails? BuildExifDetails(ExifIfd0Directory? ifd0, ExifSubIfdDirectory? sub)
    {
        if (sub == null && ifd0 == null) return null;

        var make        = ifd0?.GetDescription(ExifIfd0Directory.TagMake)?.Trim();
        var model       = ifd0?.GetDescription(ExifIfd0Directory.TagModel)?.Trim();
        var lensMake    = sub?.GetDescription(0xA433)?.Trim();   // LensMake
        var lensModel   = sub?.GetDescription(0xA434)?.Trim();   // LensModel

        var iso     = sub?.GetDescription(ExifSubIfdDirectory.TagIsoEquivalent);
        var fNum    = sub?.GetDescription(ExifSubIfdDirectory.TagFNumber);
        if (string.IsNullOrWhiteSpace(fNum))
            fNum = sub?.GetDescription(ExifSubIfdDirectory.TagAperture);
        var shutter = sub?.GetDescription(ExifSubIfdDirectory.TagExposureTime);
        var focal   = sub?.GetDescription(ExifSubIfdDirectory.TagFocalLength);
        var focal35 = sub?.GetDescription(0xA405);               // FocalLengthIn35mmFilm
        var expBias = sub?.GetDescription(0x9204);               // ExposureBiasValue
        var expProg = sub?.GetDescription(0x8822);               // ExposureProgram
        var meter   = sub?.GetDescription(0x9207);               // MeteringMode
        var flash   = sub?.GetDescription(0x9209);               // Flash
        var wb      = sub?.GetDescription(0xA403);               // WhiteBalance

        // Only return details when there is at least one photographic value.
        bool hasData = model != null || iso != null || fNum != null || shutter != null
                    || focal != null || lensModel != null;
        if (!hasData) return null;

        return new ExifDetails(
            CameraMake:      make,
            CameraModel:     model,
            LensMake:        lensMake,
            LensModel:       lensModel,
            Iso:             iso,
            Aperture:        fNum,
            ShutterSpeed:    shutter,
            FocalLength:     focal,
            FocalLength35mm: focal35,
            ExposureBias:    expBias,
            ExposureProgram: expProg,
            MeteringMode:    meter,
            Flash:           flash,
            WhiteBalance:    wb);
    }

    // ── AI generation details ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds structured AI generation details from the full set of PNG tEXt fields.
    /// Returns null when none of the known AI fields are present.
    /// </summary>
    private static AiDetails? BuildAiDetails(Dictionary<string, string> fields)
    {
        if (fields.Count == 0) return null;

        fields.TryGetValue("negative_prompt",             out var negPrompt);
        fields.TryGetValue("seed",                        out var seed);
        fields.TryGetValue("use_stable_diffusion_model",  out var model);
        fields.TryGetValue("use_vae_model",               out var vaeModel);
        fields.TryGetValue("sampler_name",                out var sampler);
        fields.TryGetValue("scheduler_name",              out var scheduler);
        fields.TryGetValue("guidance_scale",              out var guidanceScale);

        bool hasData = negPrompt != null || seed != null || model != null
                    || vaeModel != null || sampler != null || scheduler != null
                    || guidanceScale != null;
        if (!hasData) return null;

        return new AiDetails(
            NegativePrompt: string.IsNullOrWhiteSpace(negPrompt)    ? null : negPrompt,
            Seed:           string.IsNullOrWhiteSpace(seed)          ? null : seed,
            Model:          string.IsNullOrWhiteSpace(model)         ? null : model,
            VaeModel:       string.IsNullOrWhiteSpace(vaeModel)      ? null : vaeModel,
            Sampler:        string.IsNullOrWhiteSpace(sampler)       ? null : sampler,
            Scheduler:      string.IsNullOrWhiteSpace(scheduler)     ? null : scheduler,
            GuidanceScale:  string.IsNullOrWhiteSpace(guidanceScale) ? null : guidanceScale);
    }

    // ── ComfyUI workflow JSON normaliser ─────────────────────────────────────────────────────

    /// <summary>
    /// Detects a ComfyUI workflow JSON in <c>fields["prompt"]</c> and, if found, rewrites
    /// the dictionary with the extracted text values so the shared prompt-extraction and
    /// <see cref="BuildAiDetails"/> paths work without further changes.
    /// </summary>
    private static void ApplyComfyUiFields(Dictionary<string, string> fields)
    {
        if (!fields.TryGetValue("prompt", out var raw)) return;

        // ComfyUI JSON is a top-level object whose nodes each carry a "class_type" key.
        var span = raw.AsSpan().TrimStart();
        if (span.IsEmpty || span[0] != '{' || !raw.Contains("\"class_type\"", StringComparison.Ordinal))
            return;

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            string? positiveRef = null, negativeRef = null;
            string? seed = null, sampler = null, scheduler = null;
            string? guidanceScale = null, model = null, vae = null;

            // First pass: find the KSampler node — it wires positive/negative and holds settings.
            foreach (var node in root.EnumerateObject())
            {
                if (!node.Value.TryGetProperty("class_type", out var ctEl)) continue;
                var ct = ctEl.GetString();
                if (ct is not ("KSampler" or "KSamplerAdvanced")) continue;

                if (!node.Value.TryGetProperty("inputs", out var inp)) break;

                if (inp.TryGetProperty("positive", out var pos) &&
                    pos.ValueKind == JsonValueKind.Array && pos.GetArrayLength() > 0)
                    positiveRef = pos[0].GetString();

                if (inp.TryGetProperty("negative", out var neg) &&
                    neg.ValueKind == JsonValueKind.Array && neg.GetArrayLength() > 0)
                    negativeRef = neg[0].GetString();

                if (inp.TryGetProperty("seed", out var seedEl))
                    seed = seedEl.ValueKind == JsonValueKind.Number ? seedEl.GetRawText() : seedEl.GetString();
                if (inp.TryGetProperty("sampler_name", out var sampEl))
                    sampler = sampEl.GetString();
                if (inp.TryGetProperty("scheduler", out var schedEl))
                    scheduler = schedEl.GetString();
                if (inp.TryGetProperty("cfg", out var cfgEl))
                    guidanceScale = cfgEl.ValueKind == JsonValueKind.Number ? cfgEl.GetRawText() : cfgEl.GetString();

                break; // use the first KSampler found
            }

            // Second pass: resolve positive/negative text and locate model/VAE nodes.
            string? positiveText = null, negativeText = null;

            foreach (var node in root.EnumerateObject())
            {
                if (!node.Value.TryGetProperty("class_type", out var ctEl)) continue;
                var ct = ctEl.GetString();
                if (!node.Value.TryGetProperty("inputs", out var inp)) continue;

                if (ct == "CLIPTextEncode" && inp.TryGetProperty("text", out var textEl))
                {
                    var text = textEl.GetString();
                    if (node.Name == positiveRef)
                    {
                        positiveText = text;
                    }
                    else if (node.Name == negativeRef)
                    {
                        negativeText = text;
                    }
                    else if (positiveRef is null || negativeRef is null)
                    {
                        // Fallback: identify by node title when no KSampler was found.
                        var title = node.Value.TryGetProperty("_meta", out var meta) &&
                                    meta.TryGetProperty("title", out var titleEl)
                                    ? titleEl.GetString() ?? string.Empty : string.Empty;
                        if (positiveRef is null && title.Contains("Positive", StringComparison.OrdinalIgnoreCase))
                            positiveText ??= text;
                        else if (negativeRef is null && title.Contains("Negative", StringComparison.OrdinalIgnoreCase))
                            negativeText ??= text;
                    }
                }
                else if (model is null && (ct == "UNETLoader" || ct == "CheckpointLoaderSimple"))
                {
                    model = inp.TryGetProperty("unet_name", out var unetEl) ? unetEl.GetString()
                          : inp.TryGetProperty("ckpt_name",  out var ckptEl) ? ckptEl.GetString()
                          : null;
                }
                else if (vae is null && ct == "VAELoader")
                {
                    if (inp.TryGetProperty("vae_name", out var vaeEl))
                        vae = vaeEl.GetString();
                }
            }

            // Overwrite the flat fields so the shared pipeline sees plain text values.
            if (!string.IsNullOrWhiteSpace(positiveText))  fields["prompt"]                    = positiveText!;
            if (!string.IsNullOrWhiteSpace(negativeText))  fields["negative_prompt"]            = negativeText!;
            if (!string.IsNullOrWhiteSpace(seed))          fields["seed"]                       = seed!;
            if (!string.IsNullOrWhiteSpace(sampler))       fields["sampler_name"]               = sampler!;
            if (!string.IsNullOrWhiteSpace(scheduler))     fields["scheduler_name"]             = scheduler!;
            if (!string.IsNullOrWhiteSpace(guidanceScale)) fields["guidance_scale"]             = guidanceScale!;
            if (!string.IsNullOrWhiteSpace(model))         fields["use_stable_diffusion_model"] = model!;
            if (!string.IsNullOrWhiteSpace(vae))           fields["use_vae_model"]              = vae!;
        }
        catch { /* malformed JSON — leave fields unchanged */ }
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

    // ── Structured PNG chunk walker ───────────────────────────────────────────────────────────

    /// <summary>Async version used by <see cref="ReadMetadataAsync"/> — uses true async I/O
    /// so the thread pool thread is released during each file read.</summary>
    private static async Task<string?> ExtractPromptFromPngBytesAsync(string filePath)
    {
        if (!filePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                FileShare.Read, bufferSize: 4096, useAsync: true);

            var sig = new byte[8];
            if (await fs.ReadAsync(sig).ConfigureAwait(false) < 8) return null;
            if (sig[0] != 0x89 || sig[1] != 0x50 || sig[2] != 0x4E || sig[3] != 0x47) return null;

            var header = new byte[8];
            while (true)
            {
                if (await fs.ReadAsync(header).ConfigureAwait(false) < 8) break;

                int chunkLen = (header[0] << 24) | (header[1] << 16)
                             | (header[2] << 8)  |  header[3];
                string chunkType = System.Text.Encoding.ASCII.GetString(header, 4, 4);

                if (chunkType == "IDAT" || chunkType == "IEND") break;

                if ((chunkType == "tEXt" || chunkType == "iTXt") && chunkLen is > 0 and < 2_000_000)
                {
                    var chunkData = new byte[chunkLen];
                    if (await fs.ReadAsync(chunkData).ConfigureAwait(false) < chunkLen) break;

                    var result = chunkType == "tEXt"
                        ? ParseTExtChunk(chunkData)
                        : ParseITxtChunk(chunkData);

                    if (result.HasValue)
                    {
                        var (keyword, value) = result.Value;
                        var resolved = ResolveSdPromptValue(keyword, value);
                        if (!string.IsNullOrWhiteSpace(resolved))
                            return resolved!.Trim();
                    }

                    fs.Seek(4, SeekOrigin.Current); // skip CRC
                }
                else
                {
                    fs.Seek(chunkLen + 4, SeekOrigin.Current); // skip data + CRC
                }
            }
        }
        catch { /* unreadable file */ }

        return null;
    }

    /// <summary>Synchronous fallback used by the <see cref="LoadImages"/> unit-test wrapper.</summary>
    private static string? ExtractPromptFromPngBytes(string filePath)
    {
        if (!filePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                                          FileShare.Read, bufferSize: 4096);

            // Validate the 8-byte PNG signature.
            Span<byte> sig = stackalloc byte[8];
            if (fs.Read(sig) < 8) return null;
            if (sig[0] != 0x89 || sig[1] != 0x50 || sig[2] != 0x4E || sig[3] != 0x47) return null;

            Span<byte> header = stackalloc byte[8]; // 4-byte length + 4-byte type

            while (true)
            {
                if (fs.Read(header) < 8) break;

                int chunkLen = (header[0] << 24) | (header[1] << 16)
                             | (header[2] << 8)  |  header[3];
                string chunkType = System.Text.Encoding.ASCII.GetString(header[4..8]);

                // Stop before reading any pixel data — text chunks always precede IDAT.
                if (chunkType == "IDAT" || chunkType == "IEND") break;

                if ((chunkType == "tEXt" || chunkType == "iTXt") && chunkLen is > 0 and < 2_000_000)
                {
                    var chunkData = new byte[chunkLen];
                    if (fs.Read(chunkData) < chunkLen) break;

                    var result = chunkType == "tEXt"
                        ? ParseTExtChunk(chunkData)
                        : ParseITxtChunk(chunkData);

                    if (result.HasValue)
                    {
                        var (keyword, value) = result.Value;
                        var resolved = ResolveSdPromptValue(keyword, value);
                        if (!string.IsNullOrWhiteSpace(resolved))
                            return resolved!.Trim();
                    }

                    // Skip only the 4-byte CRC (data already consumed above).
                    fs.Seek(4, SeekOrigin.Current);
                }
                else
                {
                    // Skip data + 4-byte CRC without reading them.
                    fs.Seek(chunkLen + 4, SeekOrigin.Current);
                }
            }
        }
        catch { /* unreadable file — fall through */ }

        return null;
    }

    /// <summary>
    /// Parses a tEXt chunk's data bytes into (keyword, Latin-1 value).
    /// tEXt layout: keyword bytes + \0 + value bytes (Latin-1, no null terminator).
    /// </summary>
    private static (string keyword, string value)? ParseTExtChunk(byte[] data)
    {
        int nullPos = Array.IndexOf(data, (byte)0);
        if (nullPos < 1) return null;

        var keyword = System.Text.Encoding.ASCII.GetString(data, 0, nullPos);
        var value   = System.Text.Encoding.Latin1.GetString(data, nullPos + 1, data.Length - nullPos - 1);
        return (keyword, value);
    }

    /// <summary>
    /// Parses an iTXt chunk's data bytes into (keyword, UTF-8 value).
    /// iTXt layout: keyword\0 + compression_flag(1) + compression_method(1)
    ///              + language_tag\0 + translated_keyword\0 + UTF-8 text.
    /// Only uncompressed chunks (compression_flag == 0) are handled.
    /// </summary>
    private static (string keyword, string value)? ParseITxtChunk(byte[] data)
    {
        int nullPos = Array.IndexOf(data, (byte)0);
        if (nullPos < 1 || nullPos + 2 >= data.Length) return null;

        var keyword = System.Text.Encoding.ASCII.GetString(data, 0, nullPos);

        // compression_flag must be 0 for uncompressed text.
        if (data[nullPos + 1] != 0) return null;

        // Skip compression_method byte, then scan past language_tag\0 and translated_keyword\0.
        int pos = nullPos + 3; // points to start of language_tag
        int nullsNeeded = 2;
        while (pos < data.Length && nullsNeeded > 0)
        {
            if (data[pos] == 0) nullsNeeded--;
            pos++;
        }
        if (nullsNeeded > 0) return null;

        var value = System.Text.Encoding.UTF8.GetString(data, pos, data.Length - pos);
        return (keyword, value);
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
