using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Avalonia.Media.Imaging;
using Avalonia.Skia;
using PhotoComp.Models;

namespace PhotoComp.Services;

/// <summary>
/// Calls an OpenAI-compatible vision LLM to critique a photo or AI-generated image.
/// </summary>
public static class AiCriticService
{
    private static readonly HttpClient _http = new();

    private const int MaxLongEdge  = 1536;          // longest dimension cap — mirrors the JS plugin
    private const int MaxRawBytes   = 2 * 1024 * 1024; // force resize if raw file > 2 MB
    private const int TimeoutMs     = 150_000;
    private const int MaxTokens     = 2500;
    private const double Temperature = 0.3;

    // ── Public API ────────────────────────────────────────────────────────────

    public static async Task<AiCriticReport> AnalyzeAsync(
        ImageItem image,
        AiCriticSettings settings)
    {
        var endpoint = BuildEndpoint(settings.ApiUrl);
        var (base64, mimeType) = await PrepareImageAsync(image).ConfigureAwait(false);
        bool isAi = image.Prompt is not null;

        var payload = BuildPayload(image, base64, mimeType, isAi, settings.ModelName);
        var json    = JsonSerializer.Serialize(payload);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(TimeoutMs));
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

        using var response = await _http.SendAsync(request, cts.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            throw new InvalidOperationException($"API error {(int)response.StatusCode}: {body}");
        }

        var responseJson = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
        return ParseReport(responseJson, isAi, responseJson);
    }

    // ── Endpoint ──────────────────────────────────────────────────────────────

    private static string BuildEndpoint(string baseUrl)
    {
        var clean = baseUrl.TrimEnd('/');
        if (clean.EndsWith("/v1/chat/completions", StringComparison.OrdinalIgnoreCase))
            return clean;
        return clean + "/v1/chat/completions";
    }

    // ── Image preparation ─────────────────────────────────────────────────────

    private static async Task<(string Base64, string MimeType)> PrepareImageAsync(ImageItem image)
    {
        var filePath    = image.FilePath;
        bool longEdge   = image.Width > MaxLongEdge || image.Height > MaxLongEdge;

        if (!longEdge)
        {
            // Read raw bytes first; only skip resize if the file is small enough.
            var bytes = await File.ReadAllBytesAsync(filePath).ConfigureAwait(false);
            if (bytes.Length <= MaxRawBytes)
            {
                var mimeType = MimeFromExtension(Path.GetExtension(filePath));
                return (Convert.ToBase64String(bytes), mimeType);
            }
            // File is large even though dimensions are within limits — fall through to resize.
        }

        // Resize on a background thread; Bitmap ctor + CreateScaledBitmap are thread-safe.
        return await Task.Run(() => ResizeAndEncode(filePath, image.Width, image.Height))
                         .ConfigureAwait(false);
    }

    private static (string Base64, string MimeType) ResizeAndEncode(
        string filePath, int origW, int origH)
    {
        // Scale so the longest edge ≤ MaxLongEdge, preserving aspect ratio.
        // Matches the JS plugin's maxImageSize:1536 behaviour.
        double scale = (double)MaxLongEdge / Math.Max(origW, origH);
        // Don't upscale images that are already small.
        if (scale >= 1.0)
            scale = Math.Sqrt((double)(MaxRawBytes) / ((long)origW * origH * 3)); // fallback: raw-size estimate
        scale = Math.Min(scale, 1.0);
        int newW = Math.Max(1, (int)(origW * scale));
        int newH = Math.Max(1, (int)(origH * scale));

        using var src     = new Bitmap(filePath);
        using var scaled  = src.CreateScaledBitmap(new Avalonia.PixelSize(newW, newH),
                                                    BitmapInterpolationMode.MediumQuality);
        using var stream  = new MemoryStream();
        scaled.Save(stream);
        return (Convert.ToBase64String(stream.ToArray()), "image/png");
    }

    private static string MimeFromExtension(string ext) =>
        ext.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png"            => "image/png",
            ".webp"           => "image/webp",
            ".gif"            => "image/gif",
            _                 => "image/jpeg"
        };

    // ── Payload construction ──────────────────────────────────────────────────

    private static object BuildPayload(
        ImageItem image, string base64, string mimeType, bool isAi, string model)
    {
        var systemPrompt = isAi
            ? BuildAiSystemPrompt()
            : BuildPhotoSystemPrompt();

        var contextText = isAi
            ? BuildAiContext(image)
            : BuildPhotoContext(image);

        return new
        {
            model,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new
                {
                    role    = "user",
                    content = new object[]
                    {
                        new { type = "text",      text = contextText },
                        new { type = "image_url", image_url = new { url = $"data:{mimeType};base64,{base64}" } }
                    }
                }
            },
            max_tokens  = MaxTokens,
            temperature = Temperature
        };
    }

    // ── System prompts ────────────────────────────────────────────────────────

    private static string BuildAiSystemPrompt() => """
        You are an expert AI image quality analyst specialising in Stable Diffusion and similar image generators.
        Examine the provided image and identify any common AI generation artifacts or errors.

        Common artifacts to check:
        - Extra, missing, or fused fingers / limbs / toes
        - Facial anomalies: asymmetric eyes, extra eyes, distorted nose or mouth
        - Duplicate or merged body parts
        - Unnatural anatomy or impossible proportions
        - Garbled or incorrectly rendered text
        - Inconsistent or impossible lighting / shadows
        - Incoherent or melting backgrounds
        - Visible seams or stitching artifacts
        - Blurry, muddy, or over-smoothed areas
        - Unnatural or plastic-looking skin texture
        - Object merging / melting into surroundings
        - Floating or disconnected body parts
        - Hair clumping or unrealistic physics
        - Wrong number of objects (e.g. six-legged animals)
        - Incorrect perspective or scale

        Also consider the generation parameters supplied in the user message and whether they may be contributing to the issues.

        Respond ONLY with a valid JSON object matching this exact schema (no markdown fences, no extra text):
        {
          "severity": "none|minor|moderate|severe",
          "issues": [
            { "area": "<short label>", "description": "<what is wrong>", "severity": "minor|moderate|severe" }
          ],
          "positive_prompt_additions": "<comma-separated terms to ADD to the positive prompt, or empty string>",
          "negative_prompt_additions": "<comma-separated terms to ADD to the negative prompt, or empty string>",
          "parameter_suggestions": "<advice on CFG scale, steps, sampler, seed, etc., or empty string>",
          "summary": "<one or two sentence plain-English summary>"
        }
        If the image looks clean, return severity "none", an empty issues array, and explain in summary.
        """;

    private static string BuildPhotoSystemPrompt() => """
        You are an expert photography critic and photo editor.
        Examine the provided photograph and give constructive, specific feedback.

        Analyse:
        - Composition: rule of thirds, leading lines, framing, balance, subject placement
        - Exposure: over/underexposure, highlight clipping, shadow detail
        - Focus and depth of field: sharpness on subject, background blur quality
        - Lighting: quality, direction, harsh shadows, catchlights in eyes
        - Colour: white balance, colour cast, saturation
        - Noise or grain from high ISO
        - Camera shake or motion blur
        - Potential editing improvements: cropping, straightening, dodging/burning, colour grading

        Consider the EXIF data supplied in the user message when commenting on camera settings choices.

        Respond ONLY with a valid JSON object matching this exact schema (no markdown fences, no extra text):
        {
          "severity": "none|minor|moderate|severe",
          "issues": [
            { "area": "<short label>", "description": "<what could be improved>", "severity": "minor|moderate|severe" }
          ],
          "editing_suggestions": "<specific post-processing or cropping suggestions, or empty string>",
          "camera_settings_notes": "<comments on ISO, aperture, shutter speed, focal length choices, or empty string>",
          "summary": "<one or two sentence plain-English summary>"
        }
        If the photo needs no changes, return severity "none", an empty issues array, and explain in summary.
        """;

    // ── Context text ──────────────────────────────────────────────────────────

    private static string BuildAiContext(ImageItem image)
    {
        var sb = new StringBuilder("Please analyze this AI-generated image.\n\nGeneration parameters:\n");
        if (!string.IsNullOrEmpty(image.Prompt))
            sb.AppendLine($"Positive prompt: {image.Prompt}");
        var ai = image.AiDetails;
        if (ai is not null)
        {
            if (!string.IsNullOrEmpty(ai.NegativePrompt))  sb.AppendLine($"Negative prompt: {ai.NegativePrompt}");
            if (!string.IsNullOrEmpty(ai.Model))            sb.AppendLine($"Model: {ai.Model}");
            if (!string.IsNullOrEmpty(ai.VaeModel))         sb.AppendLine($"VAE: {ai.VaeModel}");
            if (!string.IsNullOrEmpty(ai.Sampler))          sb.AppendLine($"Sampler: {ai.Sampler}");
            if (!string.IsNullOrEmpty(ai.Scheduler))        sb.AppendLine($"Scheduler: {ai.Scheduler}");
            if (!string.IsNullOrEmpty(ai.GuidanceScale))    sb.AppendLine($"Guidance scale: {ai.GuidanceScale}");
            if (!string.IsNullOrEmpty(ai.Seed))             sb.AppendLine($"Seed: {ai.Seed}");
        }
        return sb.ToString();
    }

    private static string BuildPhotoContext(ImageItem image)
    {
        var sb = new StringBuilder("Please analyze this photograph.\n\nEXIF data:\n");
        var d = image.ExifDetails;
        if (d is not null)
        {
            if (!string.IsNullOrEmpty(d.CameraMake))       sb.AppendLine($"Camera make: {d.CameraMake}");
            if (!string.IsNullOrEmpty(d.CameraModel))      sb.AppendLine($"Camera model: {d.CameraModel}");
            if (!string.IsNullOrEmpty(d.LensMake))         sb.AppendLine($"Lens make: {d.LensMake}");
            if (!string.IsNullOrEmpty(d.LensModel))        sb.AppendLine($"Lens model: {d.LensModel}");
            if (!string.IsNullOrEmpty(d.Iso))              sb.AppendLine($"ISO: {d.Iso}");
            if (!string.IsNullOrEmpty(d.Aperture))         sb.AppendLine($"Aperture: {d.Aperture}");
            if (!string.IsNullOrEmpty(d.ShutterSpeed))     sb.AppendLine($"Shutter speed: {d.ShutterSpeed}");
            if (!string.IsNullOrEmpty(d.FocalLength))      sb.AppendLine($"Focal length: {d.FocalLength}");
            if (!string.IsNullOrEmpty(d.FocalLength35mm))  sb.AppendLine($"35mm equiv: {d.FocalLength35mm}");
            if (!string.IsNullOrEmpty(d.ExposureBias))     sb.AppendLine($"Exposure bias: {d.ExposureBias}");
            if (!string.IsNullOrEmpty(d.ExposureProgram))  sb.AppendLine($"Exposure program: {d.ExposureProgram}");
            if (!string.IsNullOrEmpty(d.MeteringMode))     sb.AppendLine($"Metering mode: {d.MeteringMode}");
            if (!string.IsNullOrEmpty(d.Flash))            sb.AppendLine($"Flash: {d.Flash}");
            if (!string.IsNullOrEmpty(d.WhiteBalance))     sb.AppendLine($"White balance: {d.WhiteBalance}");
        }
        if (!string.IsNullOrEmpty(image.ExifCaption))
            sb.AppendLine($"Caption: {image.ExifCaption}");
        sb.AppendLine($"Image size: {image.Width}×{image.Height}");
        return sb.ToString();
    }

    // ── Response parsing ──────────────────────────────────────────────────────

    /// <param name="fullResponseBody">The raw HTTP response body — included in the error if parsing fails.</param>
    internal static AiCriticReport ParseReport(string responseJson, bool isAi,
        string? fullResponseBody = null)
    {
        var raw = ExtractContentFromResponse(responseJson);
        var obj = ExtractJsonObject(raw)
            ?? throw new InvalidOperationException(
                $"LLM returned non-JSON content." +
                $"\n\nExtracted content ({raw.Length} chars):\n{raw}" +
                (fullResponseBody is not null && fullResponseBody != responseJson
                    ? $"\n\nFull API response body:\n{fullResponseBody}"
                    : ""));

        var severity = ParseSeverity(obj["severity"]?.GetValue<string>());

        var issuesNode = obj["issues"]?.AsArray() ?? [];
        var issues = issuesNode
            .Select(n => new AiCriticIssue(
                Area:        n?["area"]?.GetValue<string>()        ?? "",
                Description: n?["description"]?.GetValue<string>() ?? "",
                Severity:    ParseSeverity(n?["severity"]?.GetValue<string>())))
            .Where(i => !string.IsNullOrEmpty(i.Area))
            .ToList();

        var summary = obj["summary"]?.GetValue<string>() ?? "";

        if (isAi)
        {
            return new AiCriticReport(
                IsAiImage:               true,
                Severity:                severity,
                Issues:                  issues,
                Summary:                 summary,
                PositivePromptAdditions: Nz(obj["positive_prompt_additions"]?.GetValue<string>()),
                NegativePromptAdditions: Nz(obj["negative_prompt_additions"]?.GetValue<string>()),
                ParameterSuggestions:    Nz(obj["parameter_suggestions"]?.GetValue<string>()),
                EditingSuggestions:      null,
                CameraSettingsNotes:     null);
        }
        else
        {
            return new AiCriticReport(
                IsAiImage:               false,
                Severity:                severity,
                Issues:                  issues,
                Summary:                 summary,
                PositivePromptAdditions: null,
                NegativePromptAdditions: null,
                ParameterSuggestions:    null,
                EditingSuggestions:      Nz(obj["editing_suggestions"]?.GetValue<string>()),
                CameraSettingsNotes:     Nz(obj["camera_settings_notes"]?.GetValue<string>()));
        }
    }

    internal static string ExtractContentFromResponse(string responseJson)
    {
        try
        {
            var doc     = JsonNode.Parse(responseJson);
            var message = doc?["choices"]?[0]?["message"];
            if (message is null) return "";

            // Standard field.
            var content = message["content"]?.GetValue<string>()?.Trim();
            if (!string.IsNullOrEmpty(content)) return content;

            // Some LM Studio / reasoning-model servers (e.g. gemma-4-it-reap)
            // return the actual output in "reasoning_content" while leaving
            // "content" empty.
            var reasoning = message["reasoning_content"]?.GetValue<string>()?.Trim();
            if (!string.IsNullOrEmpty(reasoning)) return reasoning;

            return "";
        }
        catch
        {
            return responseJson;
        }
    }

    /// <summary>
    /// Strips thinking tags and markdown fences, then extracts the first balanced JSON object.
    /// </summary>
    internal static JsonObject? ExtractJsonObject(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var candidates = new List<string> { raw.Trim() };

        // Strip <think>…</think>, <|…|> reasoning wrappers
        var noThinking = Regex.Replace(raw, @"<\|[a-zA-Z0-9_.+-]+\|>[\s\S]*?<\|/[a-zA-Z0-9_.+-]+\|>", " ");
        noThinking     = Regex.Replace(noThinking, @"<think>[\s\S]*?</think>", " ",
                             RegexOptions.IgnoreCase).Trim();
        candidates.Add(noThinking);

        // Strip markdown fences
        var fenceMatch = Regex.Match(noThinking, @"```(?:json)?\s*([\s\S]*?)\s*```",
                             RegexOptions.IgnoreCase);
        if (fenceMatch.Success) candidates.Add(fenceMatch.Groups[1].Value.Trim());

        // Balanced brace extraction
        var balanced = ExtractFirstBalancedObject(noThinking);
        if (balanced is not null) candidates.Add(balanced);

        foreach (var c in candidates)
        {
            try { return JsonNode.Parse(c)?.AsObject(); }
            catch { /* try next */ }
        }
        return null;
    }

    private static string? ExtractFirstBalancedObject(string text)
    {
        int start = -1, depth = 0;
        bool inStr = false, esc = false;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (inStr)
            {
                if (esc)       { esc = false; continue; }
                if (ch == '\\') { esc = true;  continue; }
                if (ch == '"')    inStr = false;
                continue;
            }
            if (ch == '"')  { inStr = true; continue; }
            if (ch == '{')  { if (depth++ == 0) start = i; }
            else if (ch == '}' && depth > 0)
            {
                if (--depth == 0 && start >= 0)
                    return text[start..(i + 1)];
            }
        }
        return null;
    }

    private static AiCriticSeverity ParseSeverity(string? s) =>
        s?.ToLowerInvariant() switch
        {
            "minor"    => AiCriticSeverity.Minor,
            "moderate" => AiCriticSeverity.Moderate,
            "severe"   => AiCriticSeverity.Severe,
            _          => AiCriticSeverity.None
        };

    /// <summary>Returns null for empty/whitespace strings, otherwise the original value.</summary>
    private static string? Nz(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
