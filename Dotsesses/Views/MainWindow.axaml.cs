using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Dotsesses.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
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