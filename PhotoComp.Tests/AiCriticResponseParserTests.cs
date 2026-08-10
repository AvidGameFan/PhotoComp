using PhotoComp.Models;
using PhotoComp.Services;

namespace PhotoComp.Tests;

/// <summary>
/// Tests for AiCriticService response parsing.
/// ParseReport, ExtractContentFromResponse, and ExtractJsonObject are internal
/// and visible to this project via InternalsVisibleTo.
/// </summary>
public class AiCriticResponseParserTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Wraps a content string as a valid OpenAI-style /v1/chat/completions response body.</summary>
    private static string WrapInChoices(string content)
    {
        // Serialize so embedded quotes are escaped correctly.
        var escaped = System.Text.Json.JsonSerializer.Serialize(content);
        return "{\"choices\":[{\"message\":{\"content\":" + escaped + "}}]}";
    }

    private static string AiJson(
        string severity  = "minor",
        string positive  = "detailed fingers",
        string negative  = "extra fingers",
        string parameter = "",
        string summary   = "Minor hand artifact detected.")
    {
        return
            "{" +
            "\"severity\":\"" + severity + "\"," +
            "\"issues\":[{\"area\":\"Hands\",\"description\":\"Extra finger on left hand.\",\"severity\":\"minor\"}]," +
            "\"positive_prompt_additions\":\"" + positive + "\"," +
            "\"negative_prompt_additions\":\"" + negative + "\"," +
            "\"parameter_suggestions\":\"" + parameter + "\"," +
            "\"summary\":\"" + summary + "\"" +
            "}";
    }

    private static string PhotoJson(
        string severity = "moderate",
        string editing  = "Crop tighter on the subject.",
        string camera   = "ISO 3200 was too high; use a wider aperture.",
        string summary  = "Good composition, slightly noisy.")
    {
        return
            "{" +
            "\"severity\":\"" + severity + "\"," +
            "\"issues\":[{\"area\":\"Noise\",\"description\":\"High ISO grain.\",\"severity\":\"moderate\"}]," +
            "\"editing_suggestions\":\"" + editing + "\"," +
            "\"camera_settings_notes\":\"" + camera + "\"," +
            "\"summary\":\"" + summary + "\"" +
            "}";
    }

    // ── ExtractContentFromResponse ────────────────────────────────────────────

    [Fact]
    public void ExtractContent_ReturnsChoicesMessageContent()
    {
        var body = WrapInChoices("hello world");
        var result = AiCriticService.ExtractContentFromResponse(body);
        Assert.Equal("hello world", result);
    }

    [Fact]
    public void ExtractContent_TrimsWhitespace()
    {
        var body = WrapInChoices("  hello  ");
        var result = AiCriticService.ExtractContentFromResponse(body);
        Assert.Equal("hello", result);
    }

    [Fact]
    public void ExtractContent_ReturnsEmptyWhenContentIsNull()
    {
        const string body = """{"choices":[{"message":{"content":null}}]}""";
        var result = AiCriticService.ExtractContentFromResponse(body);
        Assert.Equal("", result);
    }

    [Fact]
    public void ExtractContent_ReturnsEmptyWhenChoicesIsMissing()
    {
        const string body = """{"id":"123","model":"llava"}""";
        var result = AiCriticService.ExtractContentFromResponse(body);
        Assert.Equal("", result);
    }
    [Fact]
    public void ExtractContent_FallsBackToReasoningContentWhenContentIsEmpty()
    {
        // Some LM Studio builds (e.g. gemma-4-it-reap) return empty "content"
        // and put the actual response in "reasoning_content".
        const string body =
            "{\"choices\":[{\"message\":{" +
            "\"content\":\"\"," +
            "\"reasoning_content\":\"{\\\"severity\\\":\\\"none\\\",\\\"issues\\\":[],\\\"summary\\\":\\\"ok\\\"}\"" +
            "}}]}";
        var result = AiCriticService.ExtractContentFromResponse(body);
        Assert.Contains("severity", result);
    }

    [Fact]
    public void ExtractContent_PrefersContentOverReasoningContent()
    {
        const string body =
            "{\"choices\":[{\"message\":{" +
            "\"content\":\"real answer\"," +
            "\"reasoning_content\":\"thinking steps\"" +
            "}}]}";
        var result = AiCriticService.ExtractContentFromResponse(body);
        Assert.Equal("real answer", result);
    }
    [Fact]
    public void ExtractContent_ReturnsFallbackWhenBodyIsNotJson()
    {
        // When the body is not JSON at all, ExtractContentFromResponse should
        // return the raw body as the fallback so nothing is silently discarded.
        const string body = "Internal server error";
        var result = AiCriticService.ExtractContentFromResponse(body);
        Assert.Equal("Internal server error", result);
    }

    // ── ExtractJsonObject ─────────────────────────────────────────────────────

    [Fact]
    public void ExtractJson_ParsesPlainJson()
    {
        var obj = AiCriticService.ExtractJsonObject(AiJson());
        Assert.NotNull(obj);
        Assert.Equal("minor", obj["severity"]?.GetValue<string>());
    }

    [Fact]
    public void ExtractJson_ParsesJsonInMarkdownFence()
    {
        var fenced = $"```json\n{AiJson()}\n```";
        var obj = AiCriticService.ExtractJsonObject(fenced);
        Assert.NotNull(obj);
        Assert.Equal("minor", obj["severity"]?.GetValue<string>());
    }

    [Fact]
    public void ExtractJson_ParsesJsonInUnlabelledMarkdownFence()
    {
        var fenced = $"```\n{AiJson()}\n```";
        var obj = AiCriticService.ExtractJsonObject(fenced);
        Assert.NotNull(obj);
        Assert.Equal("minor", obj["severity"]?.GetValue<string>());
    }

    [Fact]
    public void ExtractJson_StripsThinkTagBeforeJson()
    {
        var input = $"<think>\nLet me analyze this carefully.\n</think>\n{AiJson()}";
        var obj = AiCriticService.ExtractJsonObject(input);
        Assert.NotNull(obj);
        Assert.Equal("minor", obj["severity"]?.GetValue<string>());
    }

    [Fact]
    public void ExtractJson_StripsThinkTagCaseInsensitive()
    {
        var input = $"<THINK>reasoning here</THINK>{AiJson()}";
        var obj = AiCriticService.ExtractJsonObject(input);
        Assert.NotNull(obj);
        Assert.Equal("minor", obj["severity"]?.GetValue<string>());
    }

    [Fact]
    public void ExtractJson_ExtractsFromPrefixedText()
    {
        var input = $"Here is my analysis:\n{AiJson()}\nHope that helps!";
        var obj = AiCriticService.ExtractJsonObject(input);
        Assert.NotNull(obj);
        Assert.Equal("minor", obj["severity"]?.GetValue<string>());
    }

    [Fact]
    public void ExtractJson_HandlesImStartImEndWrappers()
    {
        // Models like Qwen emit <|im_start|>assistant\n...<|im_end|>.
        // The balanced-brace fallback must still find the JSON object.
        var input = $"<|im_start|>assistant\n{AiJson()}\n<|im_end|>";
        var obj = AiCriticService.ExtractJsonObject(input);
        Assert.NotNull(obj);
        Assert.Equal("minor", obj["severity"]?.GetValue<string>());
    }

    [Fact]
    public void ExtractJson_ReturnsNullForEmptyInput()
    {
        Assert.Null(AiCriticService.ExtractJsonObject(""));
        Assert.Null(AiCriticService.ExtractJsonObject("   "));
    }

    [Fact]
    public void ExtractJson_ReturnsNullForPlainText()
    {
        Assert.Null(AiCriticService.ExtractJsonObject("I cannot analyze this image."));
    }

    [Fact]
    public void ExtractJson_ReturnsNullForMalformedJson()
    {
        Assert.Null(AiCriticService.ExtractJsonObject("{ severity: minor }"));
    }

    // ── ParseReport — AI image path ───────────────────────────────────────────

    [Fact]
    public void ParseReport_AiPath_ParsesAllFields()
    {
        var body = WrapInChoices(AiJson(
            severity: "moderate",
            positive: "detailed hands, correct anatomy",
            negative: "extra fingers, malformed hands",
            parameter: "Lower CFG to 7.",
            summary: "Hand anatomy issue detected."));

        var report = AiCriticService.ParseReport(body, isAi: true);

        Assert.True(report.IsAiImage);
        Assert.Equal(AiCriticSeverity.Moderate, report.Severity);
        Assert.Equal("Hand anatomy issue detected.", report.Summary);
        Assert.Single(report.Issues);
        Assert.Equal("Hands", report.Issues[0].Area);
        Assert.Equal(AiCriticSeverity.Minor, report.Issues[0].Severity);
        Assert.Equal("detailed hands, correct anatomy", report.PositivePromptAdditions);
        Assert.Equal("extra fingers, malformed hands", report.NegativePromptAdditions);
        Assert.Equal("Lower CFG to 7.", report.ParameterSuggestions);
        Assert.Null(report.EditingSuggestions);
        Assert.Null(report.CameraSettingsNotes);
    }

    [Fact]
    public void ParseReport_AiPath_NoneIssues_EmptyList()
    {
        var json = """
            {
              "severity": "none",
              "issues": [],
              "positive_prompt_additions": "",
              "negative_prompt_additions": "",
              "parameter_suggestions": "",
              "summary": "Image looks clean."
            }
            """;
        var report = AiCriticService.ParseReport(WrapInChoices(json), isAi: true);
        Assert.Equal(AiCriticSeverity.None, report.Severity);
        Assert.Empty(report.Issues);
        Assert.Null(report.PositivePromptAdditions);   // empty string → null via Nz
        Assert.Equal("Image looks clean.", report.Summary);
    }

    [Fact]
    public void ParseReport_AiPath_SeverityParsedCaseInsensitive()
    {
        var json = AiJson(severity: "SEVERE");
        var report = AiCriticService.ParseReport(WrapInChoices(json), isAi: true);
        Assert.Equal(AiCriticSeverity.Severe, report.Severity);
    }

    [Fact]
    public void ParseReport_AiPath_UnknownSeverityDefaultsToNone()
    {
        var json = AiJson(severity: "critical");
        var report = AiCriticService.ParseReport(WrapInChoices(json), isAi: true);
        Assert.Equal(AiCriticSeverity.None, report.Severity);
    }

    // ── ParseReport — photo path ──────────────────────────────────────────────

    [Fact]
    public void ParseReport_PhotoPath_ParsesAllFields()
    {
        var body = WrapInChoices(PhotoJson(
            severity: "minor",
            editing: "Crop to rule of thirds.",
            camera: "ISO 800 was fine for indoor light.",
            summary: "Good shot, minor crop improvement possible."));

        var report = AiCriticService.ParseReport(body, isAi: false);

        Assert.False(report.IsAiImage);
        Assert.Equal(AiCriticSeverity.Minor, report.Severity);
        Assert.Equal("Good shot, minor crop improvement possible.", report.Summary);
        Assert.Single(report.Issues);
        Assert.Equal("Crop to rule of thirds.", report.EditingSuggestions);
        Assert.Equal("ISO 800 was fine for indoor light.", report.CameraSettingsNotes);
        Assert.Null(report.PositivePromptAdditions);
        Assert.Null(report.NegativePromptAdditions);
        Assert.Null(report.ParameterSuggestions);
    }

    [Fact]
    public void ParseReport_PhotoPath_EmptyStringsBecomNull()
    {
        var json = """
            {
              "severity": "none",
              "issues": [],
              "editing_suggestions": "",
              "camera_settings_notes": "   ",
              "summary": "Perfect photo."
            }
            """;
        var report = AiCriticService.ParseReport(WrapInChoices(json), isAi: false);
        Assert.Null(report.EditingSuggestions);
        Assert.Null(report.CameraSettingsNotes);
    }

    // ── ParseReport — failure paths ───────────────────────────────────────────

    [Fact]
    public void ParseReport_ThrowsWhenContentIsEmpty()
    {
        var body = WrapInChoices("");
        var ex = Assert.Throws<InvalidOperationException>(
            () => AiCriticService.ParseReport(body, isAi: true));
        Assert.Contains("non-JSON content", ex.Message);
        Assert.Contains("0 chars", ex.Message);
    }

    [Fact]
    public void ParseReport_ThrowsWhenContentIsPlainText()
    {
        var body = WrapInChoices("I cannot process images in this configuration.");
        var ex = Assert.Throws<InvalidOperationException>(
            () => AiCriticService.ParseReport(body, isAi: true));
        Assert.Contains("non-JSON content", ex.Message);
        // The extracted content should appear in the message for debugging.
        Assert.Contains("I cannot process", ex.Message);
    }

    [Fact]
    public void ParseReport_ErrorIncludesFullBodyWhenDifferentFromResponseJson()
    {
        // Simulate: responseJson is the choices wrapper, fullResponseBody is a
        // richer object (e.g. the raw HTTP body with extra fields). When they
        // differ, both should appear in the error.
        const string fullBody = """{"id":"abc","choices":[{"message":{"content":""}}],"model":"llava"}""";
        var ex = Assert.Throws<InvalidOperationException>(
            () => AiCriticService.ParseReport(fullBody, isAi: false, fullResponseBody: fullBody + "EXTRA"));
        Assert.Contains("Full API response body", ex.Message);
    }

    [Fact]
    public void ParseReport_WorksThroughMarkdownFencedResponse()
    {
        var fenced = $"```json\n{AiJson()}\n```";
        var body = WrapInChoices(fenced);
        var report = AiCriticService.ParseReport(body, isAi: true);
        Assert.Equal(AiCriticSeverity.Minor, report.Severity);
    }

    [Fact]
    public void ParseReport_WorksThroughThinkTaggedResponse()
    {
        var withThink = $"<think>Reasoning...</think>\n{AiJson()}";
        var body = WrapInChoices(withThink);
        var report = AiCriticService.ParseReport(body, isAi: true);
        Assert.Equal(AiCriticSeverity.Minor, report.Severity);
    }
}
