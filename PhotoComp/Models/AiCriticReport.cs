namespace PhotoComp.Models;

public enum AiCriticSeverity { None, Minor, Moderate, Severe }

public sealed record AiCriticIssue(
    string Area,
    string Description,
    AiCriticSeverity Severity);

/// <summary>
/// Result returned by the LLM critic analysis.
/// AI-image fields are populated when IsAiImage is true;
/// photo fields are populated when IsAiImage is false.
/// </summary>
public sealed record AiCriticReport(
    bool IsAiImage,
    AiCriticSeverity Severity,
    IReadOnlyList<AiCriticIssue> Issues,
    string Summary,
    // AI-image fields
    string? PositivePromptAdditions,
    string? NegativePromptAdditions,
    string? ParameterSuggestions,
    // Photo fields
    string? EditingSuggestions,
    string? CameraSettingsNotes);
