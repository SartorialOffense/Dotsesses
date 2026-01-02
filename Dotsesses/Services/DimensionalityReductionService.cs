using System.Linq;
using CSnakes.Runtime;
using CSnakes.Runtime.Python;
using Dotsesses.Models;

namespace Dotsesses.Services;

/// <summary>
/// Service for generating dimensionality reduction plots (PCA, UMAP, t-SNE) via Python integration.
/// </summary>
public class DimensionalityReductionService
{
    private readonly IDimensionalityReduction _dimRedModule;

    public DimensionalityReductionService(IPythonEnvironment env)
    {
        _dimRedModule = env.DimensionalityReduction();
    }

    /// <summary>
    /// Generates a PCA plot with the given data.
    /// </summary>
    /// <returns>Tuple of (SVG content, data points, explained variance (PC1%, PC2%))</returns>
    public (string SvgContent, List<ProjectionDataPoint> DataPoints, (double, double) ExplainedVariance) GeneratePcaPlot(
        (double Width, double Height) figSize,
        List<(string SeriesName, Dictionary<string, double> Scores)> seriesData,
        Dictionary<string, double> totalScores,
        double dotSize = 5.0,
        ThemeName theme = ThemeName.DarkMode)
    {
        var pySeriesList = seriesData
            .Select(s => (s.SeriesName, (IReadOnlyDictionary<string, double>)s.Scores.AsReadOnly()))
            .ToList();

        var themeStr = theme == ThemeName.LightMode ? "light" : "dark";

        var result = _dimRedModule.CreatePcaPlot(
            (figSize.Width, figSize.Height),
            pySeriesList,
            totalScores.AsReadOnly(),
            themeStr,
            dotSize
        );

        string svgContent = result.Item2;
        var pointDataList = result.Item3;
        var explainedVar = result.Item4;

        var dataPoints = ConvertPointData(pointDataList);

        return (svgContent, dataPoints, (explainedVar.Item1, explainedVar.Item2));
    }

    /// <summary>
    /// Generates a UMAP plot with the given data and parameters.
    /// </summary>
    public (string SvgContent, List<ProjectionDataPoint> DataPoints) GenerateUmapPlot(
        (double Width, double Height) figSize,
        List<(string SeriesName, Dictionary<string, double> Scores)> seriesData,
        Dictionary<string, double> totalScores,
        double dotSize = 5.0,
        int nNeighbors = 15,
        double minDist = 0.1,
        ThemeName theme = ThemeName.DarkMode)
    {
        var pySeriesList = seriesData
            .Select(s => (s.SeriesName, (IReadOnlyDictionary<string, double>)s.Scores.AsReadOnly()))
            .ToList();

        var themeStr = theme == ThemeName.LightMode ? "light" : "dark";

        var result = _dimRedModule.CreateUmapPlot(
            (figSize.Width, figSize.Height),
            pySeriesList,
            totalScores.AsReadOnly(),
            themeStr,
            dotSize,
            nNeighbors,
            minDist
        );

        string svgContent = result.Item2;
        var pointDataList = result.Item3;

        var dataPoints = ConvertPointData(pointDataList);

        return (svgContent, dataPoints);
    }

    /// <summary>
    /// Generates a t-SNE plot with the given data and parameters.
    /// </summary>
    public (string SvgContent, List<ProjectionDataPoint> DataPoints) GenerateTsnePlot(
        (double Width, double Height) figSize,
        List<(string SeriesName, Dictionary<string, double> Scores)> seriesData,
        Dictionary<string, double> totalScores,
        double dotSize = 5.0,
        double perplexity = 30.0,
        double learningRate = 200.0,
        ThemeName theme = ThemeName.DarkMode)
    {
        var pySeriesList = seriesData
            .Select(s => (s.SeriesName, (IReadOnlyDictionary<string, double>)s.Scores.AsReadOnly()))
            .ToList();

        var themeStr = theme == ThemeName.LightMode ? "light" : "dark";

        var result = _dimRedModule.CreateTsnePlot(
            (figSize.Width, figSize.Height),
            pySeriesList,
            totalScores.AsReadOnly(),
            themeStr,
            dotSize,
            perplexity,
            learningRate
        );

        string svgContent = result.Item2;
        var pointDataList = result.Item3;

        var dataPoints = ConvertPointData(pointDataList);

        return (svgContent, dataPoints);
    }

    private static List<ProjectionDataPoint> ConvertPointData(IReadOnlyList<PyObject> pointDataList)
    {
        var dataPoints = new List<ProjectionDataPoint>();
        foreach (var pointPyObj in pointDataList)
        {
            var pointDict = pointPyObj.As<IReadOnlyDictionary<string, PyObject>>();

            var x = pointDict["x"].As<double>();
            var y = pointDict["y"].As<double>();
            var idStr = pointDict["id"].As<string>();
            var totalScore = pointDict["total_score"].As<double>();
            var color = pointDict["color"].As<string>();

            // Parse student ID from string format "S001" -> 1
            int studentId = int.Parse(idStr.TrimStart('S'));

            dataPoints.Add(new ProjectionDataPoint(x, y, studentId, totalScore, color));
        }
        return dataPoints;
    }
}
