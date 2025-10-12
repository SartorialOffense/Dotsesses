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
    /// <param name="commentMap">Map of student IDs to their comments</param>
    /// <param name="dotSize">Size of swarm dots</param>
    /// <returns>Tuple of (SVG content string, list of data points for rendering)</returns>
    public (string SvgContent, List<ViolinDataPoint> DataPoints) GeneratePlot(
        (double Width, double Height) figSize,
        List<(string SeriesName, Dictionary<string, double> Scores)> seriesData,
        Dictionary<int, string> commentMap,
        double dotSize = 5.0)
    {
        // Convert to format expected by Python module
        var pySeriesList = seriesData
            .Select(s => (s.SeriesName, (IReadOnlyDictionary<string, double>)s.Scores.AsReadOnly()))
            .ToList();

        // Call Python module
        var result = _violinModule.CreateViolinSwarmPlot(
            (figSize.Width, figSize.Height),
            pySeriesList,
            null, // colors (use default)
            "", // title
            "", // xlabel (empty - just show series names)
            "Normalized Score (0-1)",
            dotSize
        );

        // Extract SVG string and point data
        string svgContent = result.Item2;
        var pointDataList = result.Item3;

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

            // Get comment for this student
            string comment = commentMap.TryGetValue(studentId, out string? commentValue) ? commentValue : "";

            dataPoints.Add(new ViolinDataPoint(x, y, studentId, series, color, value, comment));
        }

        return (svgContent, dataPoints);
    }
}
