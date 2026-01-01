namespace Dotsesses.UI;

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Dotsesses.Models;

/// <summary>
/// Converts a Score to its series color using the SeriesColorMap from StudentCardViewModel.
/// </summary>
public class ScoreColorConverter : IMultiValueConverter
{
    public static readonly ScoreColorConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        // values[0] = Score (from binding)
        // values[1] = SeriesColorMap (from StudentCardViewModel)
        if (values.Count < 2 || values[0] is not Score score || values[1] is not Dictionary<string, string> colorMap)
            return new SolidColorBrush(Color.Parse("#808080"));

        // Build series name from score
        var seriesName = score.Index.HasValue ? $"{score.Name} {score.Index}" : score.Name;

        // Look up color
        if (colorMap.TryGetValue(seriesName, out var hexColor))
        {
            return new SolidColorBrush(Color.Parse(hexColor));
        }

        return new SolidColorBrush(Color.Parse("#808080"));
    }
}

/// <summary>
/// Converts a Score to its series color with alpha transparency for backgrounds.
/// </summary>
public class ScoreColorWithAlphaConverter : IMultiValueConverter
{
    public static readonly ScoreColorWithAlphaConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        // values[0] = Score (from binding)
        // values[1] = SeriesColorMap (from StudentCardViewModel)
        // parameter = alpha byte (e.g., "32" for 0x20)
        if (values.Count < 2 || values[0] is not Score score || values[1] is not Dictionary<string, string> colorMap)
            return new SolidColorBrush(Color.FromArgb(0x20, 0x80, 0x80, 0x80));

        byte alpha = 0x20; // Default
        if (parameter is string alphaStr && byte.TryParse(alphaStr, out var parsedAlpha))
        {
            alpha = parsedAlpha;
        }

        // Build series name from score
        var seriesName = score.Index.HasValue ? $"{score.Name} {score.Index}" : score.Name;

        // Look up color
        if (colorMap.TryGetValue(seriesName, out var hexColor))
        {
            var baseColor = Color.Parse(hexColor);
            return new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
        }

        return new SolidColorBrush(Color.FromArgb(alpha, 0x80, 0x80, 0x80));
    }
}
