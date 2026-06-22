namespace PhotoComp.Models;

/// <summary>AI image-generation parameters extracted from PNG tEXt metadata.</summary>
public sealed record AiDetails(
    string? NegativePrompt,
    string? Seed,
    string? Model,
    string? VaeModel,
    string? Sampler,
    string? Scheduler,
    string? GuidanceScale);
