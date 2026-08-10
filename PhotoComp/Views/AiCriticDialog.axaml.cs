using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using PhotoComp.Models;

namespace PhotoComp.Views;

public partial class AiCriticDialog : Window
{
    public AiCriticDialog()
    {
        InitializeComponent();
        CloseButton.Click += (_, _) => Close();
    }

    /// <summary>Populate the dialog with the analysis result (called after the async LLM call).</summary>
    public void ShowReport(AiCriticReport report)
    {
        LoadingPanel.IsVisible = false;
        ErrorPanel.IsVisible   = false;
        ResultPanel.IsVisible  = true;

        // Severity badge
        SeverityText.Text = report.Severity.ToString();
        SeverityBadge.Background = SeverityColor(report.Severity);

        SummaryText.Text = report.Summary;

        // Issues
        IssuesList.ItemsSource = report.Issues;
        NoIssuesText.IsVisible = report.Issues.Count == 0;

        if (report.IsAiImage)
        {
            AiPanel.IsVisible = true;
            PositiveBox.Text  = report.PositivePromptAdditions ?? "";
            NegativeBox.Text  = report.NegativePromptAdditions ?? "";

            CopyPositiveBtn.Click += (_, _) => CopyText(PositiveBox.Text, CopyPositiveBtn);
            CopyNegativeBtn.Click += (_, _) => CopyText(NegativeBox.Text, CopyNegativeBtn);

            if (!string.IsNullOrEmpty(report.ParameterSuggestions))
            {
                ParamHeader.IsVisible = true;
                ParamText.IsVisible   = true;
                ParamText.Text        = report.ParameterSuggestions;
            }
        }
        else
        {
            PhotoPanel.IsVisible = true;
            if (!string.IsNullOrEmpty(report.EditingSuggestions))
            {
                EditingHeader.IsVisible = true;
                EditingText.IsVisible   = true;
                EditingText.Text        = report.EditingSuggestions;
            }
            if (!string.IsNullOrEmpty(report.CameraSettingsNotes))
            {
                CameraHeader.IsVisible = true;
                CameraText.IsVisible   = true;
                CameraText.Text        = report.CameraSettingsNotes;
            }
        }
    }

    /// <summary>Switch to the error state.</summary>
    public void ShowError(string message)
    {
        LoadingPanel.IsVisible = false;
        ErrorPanel.IsVisible   = true;
        ErrorText.Text         = message;
    }

    private async void CopyText(string? text, Button btn)
    {
        if (string.IsNullOrEmpty(text) || Clipboard is null) return;
        try
        {
            var item     = DataTransferItem.Create(DataFormat.Text, text);
            var transfer = new DataTransfer();
            transfer.Add(item);
            await Clipboard.SetDataAsync(transfer);
            var original = btn.Content?.ToString() ?? "Copy";
            btn.Content = "Copied!";
            await Task.Delay(2000);
            btn.Content = original;
        }
        catch { /* clipboard unavailable — swallow silently */ }
    }

    private static IBrush SeverityColor(AiCriticSeverity severity) =>
        severity switch
        {
            AiCriticSeverity.None     => new SolidColorBrush(Color.Parse("#28a745")),
            AiCriticSeverity.Minor    => new SolidColorBrush(Color.Parse("#ffc107")),
            AiCriticSeverity.Moderate => new SolidColorBrush(Color.Parse("#fd7e14")),
            AiCriticSeverity.Severe   => new SolidColorBrush(Color.Parse("#dc3545")),
            _                         => new SolidColorBrush(Color.Parse("#6c757d"))
        };
}
