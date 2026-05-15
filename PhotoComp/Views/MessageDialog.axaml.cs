using Avalonia.Controls;

namespace PhotoComp.Views;

public partial class MessageDialog : Window
{
    // Parameterless ctor required by Avalonia's XAML runtime loader.
    public MessageDialog()
    {
        InitializeComponent();
        OkButton.Click += (_, _) => Close();
    }

    public MessageDialog(string title, string message) : this()
    {
        Title = title;
        MessageText.Text = message;
    }
}
