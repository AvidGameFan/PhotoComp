using Avalonia.Controls;
using PhotoComp.Models;
using PhotoComp.Services;

namespace PhotoComp.Views;

public partial class AiCriticSettingsDialog : Window
{
    public bool Saved { get; private set; }

    public AiCriticSettingsDialog() : this(SettingsService.LoadSettings()) { }

    public AiCriticSettingsDialog(AiCriticSettings current)
    {
        InitializeComponent();
        UrlBox.Text   = current.ApiUrl;
        KeyBox.Text   = current.ApiKey;
        ModelBox.Text = current.ModelName;

        SaveButton.Click   += (_, _) => { Saved = true; Close(); };
        CancelButton.Click += (_, _) => Close();
    }

    public AiCriticSettings BuildSettings() =>
        new(ApiUrl:    UrlBox.Text?.Trim()   ?? "",
            ApiKey:    KeyBox.Text?.Trim()   ?? "",
            ModelName: ModelBox.Text?.Trim() ?? "");
}
