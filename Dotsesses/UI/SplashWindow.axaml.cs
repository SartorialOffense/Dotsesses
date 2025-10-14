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
        // Write to file for debugging since console isn't available in WinExe
        try
        {
            var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dotsesses_startup.log");
            System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] UpdateStatus: {message}\n");
        }
        catch { }

        if (LoadingText != null)
        {
            LoadingText.Text = message;
        }
    }
}
