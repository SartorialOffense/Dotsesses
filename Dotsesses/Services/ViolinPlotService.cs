using CSnakes.Runtime;
using CSnakes.Runtime.Python;
using Dotsesses.Models;

namespace Dotsesses.Services;

/// <summary>
/// Service for generating violin plots via Python integration.
/// </summary>
public class ViolinPlotService
{
    private readonly IViolinSwarm _violinModule;

    public ViolinPlotService(IPythonEnvironment env)
    {
        _violinModule = env.ViolinSwarm();
    }

    /// <summary>
    /// Generates a violin plot with the given data.
    /// </summary>
    /// <param name="figSize">Figure size in inches (width, height)</param>
    /// <param name="seriesData">List of (series name, student ID to score mapping)</param>
    /// <param name="commentMap">Map of (student ID, series name) to score comment</param>
    /// <param name="muppetNameMap">Map of student ID to muppet name</param>
    /// <param name="dotSize">Size of swarm dots</param>
    /// <param name="swarmSpread">Multiplier for horizontal spread of swarm dots (default 1.0, increase to spread wider)</param>
    /// <param name="theme">Theme name for rendering ('dark' or 'light')</param>
    /// <param name="ordinalLabelMap">Optional map of (student ID, series name) to the
    /// Ordinal column's display label (ADR-0017); when present the point's
    /// <see cref="ViolinDataPoint.ValueLabel"/> carries it so hover shows the label
    /// (e.g. "✔✔+") instead of the numeric SortOrder.</param>
    /// <returns>Tuple of (SVG content string, list of data points for rendering)</returns>
    public (string SvgContent, List<ViolinDataPoint> DataPoints) GeneratePlot(
        (double Width, double Height) figSize,
        List<(string SeriesName, Dictionary<string, double> Scores)> seriesData,
        Dictionary<(int StudentId, string SeriesName), string> commentMap,
        Dictionary<int, string> muppetNameMap,
        double dotSize = 5.0,
        double swarmSpread = 1.0,
        ThemeName theme = ThemeName.DarkMode,
        Dictionary<(int StudentId, string SeriesName), string>? ordinalLabelMap = null)
    {
        // Convert to format expected by Python module
        var pySeriesList = seriesData
            .Select(s => (s.SeriesName, (IReadOnlyDictionary<string, double>)s.Scores.AsReadOnly()))
            .ToList();

        // Convert theme enum to Python string
        var themeStr = theme == ThemeName.LightMode ? "light" : "dark";

        // Call Python module
        var result = _violinModule.CreateViolinSwarmPlot(
            (figSize.Width, figSize.Height),
            pySeriesList,
            null, // colors (use default)
            "", // title
            "", // xlabel (empty - just show series names)
            "Normalized Score (0-1)",
            dotSize,
            swarmSpread,
            themeStr
        );

        // Extract SVG string and point data
        string svgContent = result.Item2;
        var pointDataList = result.Item3;

        // Calculate mean and std dev per series for sigma values
        var seriesStats = seriesData.ToDictionary(
            s => s.SeriesName,
            s =>
            {
                var values = s.Scores.Values.ToList();
                var mean = values.Average();
                var stdDev = Math.Sqrt(values.Average(v => Math.Pow(v - mean, 2)));
                return (Mean: mean, StdDev: stdDev);
            });

        // Convert PyObject point data to ViolinDataPoint records
        var dataPoints = new List<ViolinDataPoint>();
        foreach (var pointPyObj in pointDataList)
        {
            var pointDict = pointPyObj.As<IReadOnlyDictionary<string, PyObject>>();

            var x = pointDict["x"].As<double>();
            var y = pointDict["y"].As<double>();
            var idStr = pointDict["id"].As<string>();
            var series = pointDict["series"].As<string>();
            var color = pointDict["color"].As<string>();
            var value = pointDict["value"].As<double>();

            // Parse student ID from string format "S001" -> 1
            int studentId = int.Parse(idStr.TrimStart('S'));

            // Get comment for this specific score (student + series)
            string comment = commentMap.TryGetValue((studentId, series), out string? commentValue) ? commentValue : "";

            // Get muppet name for this student
            string muppetName = muppetNameMap.TryGetValue(studentId, out string? name) ? name : "";

            // Ordinal series: the hover label (e.g. "✔✔+") for this point's value.
            string valueLabel = ordinalLabelMap != null &&
                ordinalLabelMap.TryGetValue((studentId, series), out string? lbl) ? lbl : "";

            // Calculate sigma value for this point's series
            var stats = seriesStats[series];
            var sigmaValue = stats.StdDev > 0 ? (value - stats.Mean) / stats.StdDev : 0;

            dataPoints.Add(new ViolinDataPoint(x, y, studentId, series, color, value, sigmaValue, comment, muppetName, valueLabel));
        }

        return (svgContent, dataPoints);
    }
}
