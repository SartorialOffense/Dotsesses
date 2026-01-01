namespace Dotsesses.Services;

using System.Collections.Generic;

/// <summary>
/// Generates series-to-color mappings for consistent color coding.
/// Replicates the color palette logic from violin_swarm.py.
/// </summary>
public static class SeriesColorService
{
    // Cycling palette for non-Total series (matches Python violin_swarm.py)
    private static readonly string[] CyclingPalette =
    {
        "#0066FF",  // Bright blue
        "#FF6600",  // Bright orange
        "#00CC00",  // Bright green
        "#FF00FF",  // Bright magenta
        "#9933FF",  // Bright purple
        "#00CCCC",  // Bright cyan
        "#FFCC00",  // Bright yellow
    };

    // Red reserved for Total (last series)
    private const string TotalColor = "#FF3333";

    /// <summary>
    /// Generates a dictionary mapping series names to hex color strings.
    /// Last series (Total) is always red, others cycle through the palette.
    /// </summary>
    /// <param name="seriesNames">Ordered list of series names</param>
    /// <returns>Dictionary mapping series name to hex color</returns>
    public static Dictionary<string, string> GenerateColorMap(IList<string> seriesNames)
    {
        var colorMap = new Dictionary<string, string>();

        if (seriesNames.Count == 0)
            return colorMap;

        // All but last series cycle through palette
        for (int i = 0; i < seriesNames.Count - 1; i++)
        {
            colorMap[seriesNames[i]] = CyclingPalette[i % CyclingPalette.Length];
        }

        // Last series (Total) is always red
        colorMap[seriesNames[^1]] = TotalColor;

        return colorMap;
    }
}
