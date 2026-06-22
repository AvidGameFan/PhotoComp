using PhotoComp.Models;
using PhotoComp.Services;

namespace PhotoComp.Tests;

/// <summary>
/// End-to-end tests for AI metadata extraction through <see cref="ImageLoaderService.LoadImages"/>.
/// Each test writes a real minimal PNG file with crafted tEXt chunks so the full
/// MetadataExtractor → ApplyComfyUiFields / BuildAiDetails pipeline is exercised.
/// </summary>
public sealed class AiMetadataTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public AiMetadataTests() => Directory.CreateDirectory(_tempDir);
    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    // ── ComfyUI test data ──────────────────────────────────────────────────────────────────────

    // Exact JSON provided in the feature request (node 60:11 = positive, 60:12 = negative).
    private const string ComfyJson = """
        {"46": {"inputs": {"filename_prefix": "Anima", "images": ["60:8", 0]}, "class_type": "SaveImage", "_meta": {"title": "Save Image"}}, "60:8": {"inputs": {"samples": ["60:19", 0], "vae": ["60:15", 0]}, "class_type": "VAEDecode", "_meta": {"title": "VAE Decode"}}, "60:28": {"inputs": {"width": 1024, "height": 1024, "batch_size": 1}, "class_type": "EmptyLatentImage", "_meta": {"title": "Empty Latent Image"}}, "60:12": {"inputs": {"text": "worst quality, low quality, score_1, score_2, score_3, blurry, jpeg artifacts, sepia", "clip": ["60:45", 0]}, "class_type": "CLIPTextEncode", "_meta": {"title": "CLIP Text Encode (Negative Prompt)"}}, "60:19": {"inputs": {"seed": 458880083158, "steps": 30, "cfg": 4.0, "sampler_name": "er_sde", "scheduler": "simple", "denoise": 1.0, "model": ["60:44", 0], "positive": ["60:11", 0], "negative": ["60:12", 0], "latent_image": ["60:28", 0]}, "class_type": "KSampler", "_meta": {"title": "KSampler"}}, "60:11": {"inputs": {"text": "masterpiece, best quality, person shopping in town, fantasy, anime style, cartoon animation", "clip": ["60:45", 0]}, "class_type": "CLIPTextEncode", "_meta": {"title": "CLIP Text Encode (Positive Prompt)"}}, "60:44": {"inputs": {"unet_name": "anima-preview3-base.safetensors", "weight_dtype": "default"}, "class_type": "UNETLoader", "_meta": {"title": "Load Diffusion Model"}}, "60:15": {"inputs": {"vae_name": "Qwen_Image-VAE.safetensors"}, "class_type": "VAELoader", "_meta": {"title": "Load VAE"}}, "60:45": {"inputs": {"clip_name": "qwen_3_06b_base.safetensors", "type": "stable_diffusion", "device": "default"}, "class_type": "CLIPLoader", "_meta": {"title": "Load CLIP"}}}
        """;

    private const string ComfyPositivePrompt =
        "masterpiece, best quality, person shopping in town, fantasy, anime style, cartoon animation";

    private const string ComfyNegativePrompt =
        "worst quality, low quality, score_1, score_2, score_3, blurry, jpeg artifacts, sepia";

    // ── PNG test-file builder ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes a valid PNG to <c>_tempDir</c> containing the given tEXt chunks, then
    /// runs <see cref="ImageLoaderService.LoadImages"/> and returns the single item.
    /// </summary>
    private ImageItem LoadSinglePng(string name, params (string keyword, string value)[] chunks)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, BuildMinimalPng(chunks));
        return ImageLoaderService.LoadImages(_tempDir).Single();
    }

    private static byte[] BuildMinimalPng(IEnumerable<(string keyword, string value)> textChunks)
    {
        using var ms = new MemoryStream();

        // PNG signature
        ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        // IHDR: 1×1 grayscale-8 image
        WriteChunk(ms, "IHDR", [
            0, 0, 0, 1,  // width  = 1
            0, 0, 0, 1,  // height = 1
            8,           // bit depth
            0,           // color type: grayscale
            0,           // compression method
            0,           // filter method
            0            // interlace method
        ]);

        foreach (var (keyword, value) in textChunks)
        {
            var kw  = System.Text.Encoding.Latin1.GetBytes(keyword);
            var val = System.Text.Encoding.Latin1.GetBytes(value);
            var data = new byte[kw.Length + 1 + val.Length];
            kw.CopyTo(data, 0);
            data[kw.Length] = 0; // null separator
            val.CopyTo(data, kw.Length + 1);
            WriteChunk(ms, "tEXt", data);
        }

        WriteChunk(ms, "IEND", []);
        return ms.ToArray();
    }

    private static void WriteChunk(MemoryStream ms, string type, byte[] data)
    {
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        var len = data.Length;
        ms.WriteByte((byte)(len >> 24));
        ms.WriteByte((byte)(len >> 16));
        ms.WriteByte((byte)(len >> 8));
        ms.WriteByte((byte)len);
        ms.Write(typeBytes);
        ms.Write(data);

        // CRC32 over chunk-type bytes + data bytes
        var crcBuf = new byte[typeBytes.Length + data.Length];
        typeBytes.CopyTo(crcBuf, 0);
        data.CopyTo(crcBuf, typeBytes.Length);
        var crc = Crc32(crcBuf);
        ms.WriteByte((byte)(crc >> 24));
        ms.WriteByte((byte)(crc >> 16));
        ms.WriteByte((byte)(crc >> 8));
        ms.WriteByte((byte)crc);
    }

    /// <summary>Standard CRC-32 (ISO 3309) as required by the PNG specification.</summary>
    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }

    // ── ComfyUI workflow JSON parsing ──────────────────────────────────────────────────────────

    [Fact]
    public void ComfyUi_PositivePrompt_IsExtracted()
    {
        var item = LoadSinglePng("comfy.png", ("prompt", ComfyJson));

        // Prompt must be the positive text node, not the raw JSON blob.
        Assert.Equal(ComfyPositivePrompt, item.Prompt);
    }

    [Fact]
    public void ComfyUi_NegativePrompt_IsInAiDetails()
    {
        var item = LoadSinglePng("comfy.png", ("prompt", ComfyJson));

        Assert.Equal(ComfyNegativePrompt, item.AiDetails?.NegativePrompt);
    }

    [Fact]
    public void ComfyUi_Seed_IsInAiDetails()
    {
        var item = LoadSinglePng("comfy.png", ("prompt", ComfyJson));

        Assert.Equal("458880083158", item.AiDetails?.Seed);
    }

    [Fact]
    public void ComfyUi_Sampler_IsInAiDetails()
    {
        var item = LoadSinglePng("comfy.png", ("prompt", ComfyJson));

        Assert.Equal("er_sde", item.AiDetails?.Sampler);
    }

    [Fact]
    public void ComfyUi_Scheduler_IsInAiDetails()
    {
        var item = LoadSinglePng("comfy.png", ("prompt", ComfyJson));

        Assert.Equal("simple", item.AiDetails?.Scheduler);
    }

    [Fact]
    public void ComfyUi_GuidanceScale_IsInAiDetails()
    {
        var item = LoadSinglePng("comfy.png", ("prompt", ComfyJson));

        // cfg = 4.0 in JSON; GetRawText() preserves the original token.
        Assert.Equal("4.0", item.AiDetails?.GuidanceScale);
    }

    [Fact]
    public void ComfyUi_Model_IsInAiDetails()
    {
        var item = LoadSinglePng("comfy.png", ("prompt", ComfyJson));

        Assert.Equal("anima-preview3-base.safetensors", item.AiDetails?.Model);
    }

    [Fact]
    public void ComfyUi_Vae_IsInAiDetails()
    {
        var item = LoadSinglePng("comfy.png", ("prompt", ComfyJson));

        Assert.Equal("Qwen_Image-VAE.safetensors", item.AiDetails?.VaeModel);
    }

    // ── Easy Diffusion flat tEXt fields ───────────────────────────────────────────────────────

    [Fact]
    public void EasyDiffusion_Prompt_IsExtracted()
    {
        const string text = "a golden retriever at the beach";
        var item = LoadSinglePng("ed.png", ("prompt", text));

        Assert.Equal(text, item.Prompt);
    }

    [Fact]
    public void EasyDiffusion_AllAiFields_ArePopulated()
    {
        var item = LoadSinglePng("ed_full.png",
            ("prompt",                    "a cat"),
            ("negative_prompt",           "blurry"),
            ("seed",                      "99887766"),
            ("use_stable_diffusion_model","dreamshaper.safetensors"),
            ("use_vae_model",             "vae-ft-mse.safetensors"),
            ("sampler_name",              "euler_a"),
            ("scheduler_name",            "karras"),
            ("guidance_scale",            "7.5"));

        Assert.Equal("a cat",                   item.Prompt);
        Assert.Equal("blurry",                  item.AiDetails?.NegativePrompt);
        Assert.Equal("99887766",                item.AiDetails?.Seed);
        Assert.Equal("dreamshaper.safetensors", item.AiDetails?.Model);
        Assert.Equal("vae-ft-mse.safetensors",  item.AiDetails?.VaeModel);
        Assert.Equal("euler_a",                 item.AiDetails?.Sampler);
        Assert.Equal("karras",                  item.AiDetails?.Scheduler);
        Assert.Equal("7.5",                     item.AiDetails?.GuidanceScale);
    }

    // ── Guard / edge-case tests ────────────────────────────────────────────────────────────────

    [Fact]
    public void NonAiPng_AiDetails_IsNull()
    {
        // A PNG with no recognised AI tEXt keywords must produce null AiDetails.
        var item = LoadSinglePng("plain.png", ("Comment", "taken with phone"));

        Assert.Null(item.AiDetails);
    }

    [Fact]
    public void PlainTextPrompt_IsNotMangled_ByComfyUiParser()
    {
        // A plain-text "prompt" value (not JSON) must pass through unchanged.
        const string plain = "a serene mountain lake at sunrise";
        var item = LoadSinglePng("plain_prompt.png", ("prompt", plain));

        Assert.Equal(plain, item.Prompt);
    }
}
