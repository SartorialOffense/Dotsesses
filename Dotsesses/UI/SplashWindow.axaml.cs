using Avalonia.Controls;

namespace Dotsesses.UI;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
    }

    public void UpdateStatus(string message)
    {
        if (LoadingText != null)
        {
            LoadingText.Text = message;
        }
    }
}
