using System.Text.Json;
using System.Text.Json.Serialization;
using PhotoComp.Models;

namespace PhotoComp.Services;

/// <summary>Persists AiCriticSettings to %AppData%/PhotoComp/settings.json.</summary>
public static class SettingsService
{
    private static readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PhotoComp",
        "settings.json");

    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static AiCriticSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(_path)) return AiCriticSettings.Default;
            var text = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<SettingsDto>(text) is { } dto
                ? new AiCriticSettings(dto.ApiUrl ?? "", dto.ApiKey ?? "", dto.ModelName ?? "")
                : AiCriticSettings.Default;
        }
        catch
        {
            return AiCriticSettings.Default;
        }
    }

    public static void SaveSettings(AiCriticSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var dto = new SettingsDto
        {
            ApiUrl    = settings.ApiUrl,
            ApiKey    = settings.ApiKey,
            ModelName = settings.ModelName
        };
        File.WriteAllText(_path, JsonSerializer.Serialize(dto, _json));
    }

    // Separate DTO keeps the public model as a clean record.
    private sealed class SettingsDto
    {
        public string? ApiUrl    { get; set; }
        public string? ApiKey    { get; set; }
        public string? ModelName { get; set; }
    }
}
