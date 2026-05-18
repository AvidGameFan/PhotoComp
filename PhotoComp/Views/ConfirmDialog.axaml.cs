using Avalonia.Controls;

namespace PhotoComp.Views;

public partial class ConfirmDialog : Window
{
    public bool Result { get; private set; }

    // Parameterless ctor required by Avalonia's XAML runtime loader.
    public ConfirmDialog()
    {
        InitializeComponent();
        YesButton.Click += (_, _) => { Result = true;  Close(); };
        NoButton.Click  += (_, _) => { Result = false; Close(); };
    }

    public ConfirmDialog(string title, string message) : this()
    {
        Title = title;
        MessageText.Text = message;
    }
}
