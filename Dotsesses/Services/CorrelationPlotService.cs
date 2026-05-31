using System.Linq;
using CSnakes.Runtime;
using CSnakes.Runtime.Python;
using Dotsesses.Models;

namespace Dotsesses.Services;

/// <summary>
/// Service for generating correlation matrix plots via Python integration.
/// </summary>
public class CorrelationPlotService
{
    private readonly ICorrelationMatrix _correlationModule;

    public CorrelationPlotService(IPythonEnvironment env)
    {
        _correlationModule = env.CorrelationMatrix();
    }

    /// <summary>
    /// Generates a correlation matrix plot with the given data.
    /// </summary>
    /// <param name="figSize">Figure size in inches (width, height)</param>
    /// <param name="seriesData">List of (series name, student ID to score mapping)</param>
    /// <param name="columnMetadata">
    /// Per-series column metadata keyed by series name (type / aggregate-component
    /// / Total flag). Lets Python key behavior off explicit flags rather than
    /// series position (ADR-0018 slice 1). Series absent from the map default to
    /// Numeric / not-a-component, with Total inferred by name as a fallback.
    /// </param>
    /// <param name="muppetNameMap">Map of student ID to muppet name</param>
    /// <param name="dotSize">Size of scatter dots</param>
    /// <param name="showCorrelationCoefficients">Whether to show r values</param>
    /// <param name="diagonalType">Type of diagonal plot: "kde" or "hist"</param>
    /// <param name="theme">Theme name for rendering ('dark' or 'light')</param>
    /// <returns>Tuple of (SVG content string, list of data points for rendering)</returns>
    public (string SvgContent, List<CorrelationDataPoint> DataPoints) GeneratePlot(
        (double Width, double Height) figSize,
        List<(string SeriesName, Dictionary<string, double> Scores)> seriesData,
        IReadOnlyDictionary<string, CorrelationColumnInfo> columnMetadata,
        Dictionary<int, string> muppetNameMap,
        double dotSize = 3.0,
        bool showCorrelationCoefficients = true,
        string diagonalType = "kde",
        ThemeName theme = ThemeName.DarkMode)
    {
        // Convert to format expected by Python module
        var pySeriesList = seriesData
            .Select(s => (s.SeriesName, (IReadOnlyDictionary<string, double>)s.Scores.AsReadOnly()))
            .ToList();

        // Per-series metadata as parallel lists aligned with pySeriesList order
        // (ADR-0018 slice 1). A series with no metadata entry falls back to a
        // Numeric non-component; Total is still detected by name so the red
        // styling survives the no-selection passthrough path.
        var columnTypes = new List<string>(seriesData.Count);
        var isAggregateComponent = new List<bool>(seriesData.Count);
        var isTotal = new List<bool>(seriesData.Count);
        foreach (var (name, _) in seriesData)
        {
            if (columnMetadata.TryGetValue(name, out var info))
            {
                columnTypes.Add(ColumnTypeToPython(info.Type));
                isAggregateComponent.Add(info.IsAggregateComponent);
                isTotal.Add(info.IsTotal);
            }
            else
            {
                columnTypes.Add("numeric");
                isAggregateComponent.Add(false);
                isTotal.Add(name.Equals("Total", StringComparison.OrdinalIgnoreCase));
            }
        }

        // Convert theme enum to Python string
        var themeStr = theme == ThemeName.LightMode ? "light" : "dark";

        // Call Python module
        var result = _correlationModule.CreateCorrelationMatrix(
            (figSize.Width, figSize.Height),
            pySeriesList,
            columnTypes,
            isAggregateComponent,
            isTotal,
            themeStr,
            dotSize,
            showCorrelationCoefficients,
            diagonalType
        );

        // Extract SVG string and point data
        string svgContent = result.Item2;
        var pointDataList = result.Item3;

        // Convert PyObject point data to CorrelationDataPoint records
        var dataPoints = new List<CorrelationDataPoint>();
        foreach (var pointPyObj in pointDataList)
        {
            var pointDict = pointPyObj.As<IReadOnlyDictionary<string, PyObject>>();

            var cellRow = pointDict["cell_row"].As<int>();
            var cellCol = pointDict["cell_col"].As<int>();
            var x = pointDict["x"].As<double>();
            var y = pointDict["y"].As<double>();
            var idStr = pointDict["id"].As<string>();
            var xSeries = pointDict["x_series"].As<string>();
            var ySeries = pointDict["y_series"].As<string>();
            var xValue = pointDict["x_value"].As<double>();
            var yValue = pointDict["y_value"].As<double>();
            var color = pointDict["color"].As<string>();

            // Parse student ID from string format "S001" -> 1
            int studentId = int.Parse(idStr.TrimStart('S'));

            // Get muppet name for this student
            string muppetName = muppetNameMap.TryGetValue(studentId, out string? name) ? name : "";

            dataPoints.Add(new CorrelationDataPoint(
                cellRow, cellCol, x, y, studentId,
                xSeries, ySeries, xValue, yValue, color, muppetName));
        }

        return (svgContent, dataPoints);
    }

    /// <summary>
    /// Maps a <see cref="ScoreColumnType"/> to the lowercase token the Python
    /// renderer expects in its per-series <c>column_types</c> list.
    /// </summary>
    private static string ColumnTypeToPython(ScoreColumnType type) => type switch
    {
        ScoreColumnType.Categorical => "categorical",
        ScoreColumnType.Ordinal => "ordinal",
        _ => "numeric",
    };
}
