namespace PhotoComp.Models;

public sealed record AiCriticSettings(
    string ApiUrl,
    string ApiKey,
    string ModelName)
{
    public static AiCriticSettings Default { get; } = new(
        ApiUrl:    "http://localhost:1234",
        ApiKey:    "",
        ModelName: "llava");
}
