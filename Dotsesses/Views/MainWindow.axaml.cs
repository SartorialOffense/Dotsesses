using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Dotsesses.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Saves a PNG snapshot of the window to the specified path or temp folder.
    /// </summary>
    /// <param name="outputPath">Optional output path. If null, saves to temp folder with timestamp.</param>
    /// <returns>The full file path where the snapshot was saved.</returns>
    public async Task<string> SaveSnapshotAsync(string? outputPath = null)
    {
        // Determine output path
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            outputPath = Path.Combine(Path.GetTempPath(), $"dotsesses_snapshot_{timestamp}.png");
        }

        // Ensure directory exists
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Wait a moment to ensure rendering is complete
        await Task.Delay(200);

        // Force layout update
        UpdateLayout();

        // Create bitmap with window dimensions
        var pixelSize = new PixelSize((int)Bounds.Width, (int)Bounds.Height);
        var dpiVector = new Vector(96, 96);

        using var bitmap = new RenderTargetBitmap(pixelSize, dpiVector);
        bitmap.Render(this);

        // Save to file with maximum quality
        bitmap.Save(outputPath, 100);

        return outputPath;
    }
}

/// <summary>
/// Converts signed deviation to appropriate color: negative = light blue, positive = red.
/// </summary>
public class DeviationColorConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count > 0 && values[0] is int signedDeviation)
        {
            if (signedDeviation < 0)
            {
                // Negative deviation (below target) - light blue
                return new SolidColorBrush(Color.FromRgb(100, 180, 230));
            }
            else if (signedDeviation > 0)
            {
                // Positive deviation (above target) - red
                return new SolidColorBrush(Color.FromRgb(255, 107, 107));
            }
        }

        return new SolidColorBrush(Colors.White);
    }

    public object?[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}