using System.Linq;
using CSnakes.Runtime;
using CSnakes.Runtime.Python;
using Dotsesses.Models;

namespace Dotsesses.Services;

/// <summary>
/// Generates Significance Matrix plots via the Python module
/// (Dotsesses/Python/Violin/significance_matrix.py). Returns an SVG string
/// and a list of <see cref="SignificanceDataPoint"/> entries that the
/// <c>SignificancePlotControl</c> Canvas overlay uses for hover hit-testing.
/// </summary>
public class SignificancePlotService
{
    private readonly ISignificanceMatrix _module;

    public SignificancePlotService(IPythonEnvironment env)
    {
        _module = env.SignificanceMatrix();
    }

    /// <summary>
    /// Generates a significance matrix plot.
    /// </summary>
    /// <param name="figSize">Figure size in inches (width, height).</param>
    /// <param name="numericSeries">List of (column name, {student id → numeric value}).</param>
    /// <param name="categoricalSeries">List of (column name, {student id → subgroup string}).</param>
    /// <param name="dotSize">Marker radius in points.</param>
    /// <param name="theme">Theme name (dark / light).</param>
    /// <param name="testFamily">Omnibus test run per cell (see ADR-0014).</param>
    public (string SvgContent, List<SignificanceDataPoint> DataPoints) GeneratePlot(
        (double Width, double Height) figSize,
        List<(string Name, Dictionary<string, double> Values)> numericSeries,
        List<(string Name, Dictionary<string, string> Subgroups)> categoricalSeries,
        double dotSize = 5.0,
        ThemeName theme = ThemeName.DarkMode,
        SignificanceTestFamily testFamily = SignificanceTestFamily.Parametric)
    {
        var pyNumeric = numericSeries
            .Select(s => (s.Name, (IReadOnlyDictionary<string, double>)s.Values.AsReadOnly()))
            .ToList();
        var pyCategorical = categoricalSeries
            .Select(s => (s.Name, (IReadOnlyDictionary<string, string>)s.Subgroups.AsReadOnly()))
            .ToList();

        var themeStr = theme == ThemeName.LightMode ? "light" : "dark";
        var testFamilyStr = testFamily == SignificanceTestFamily.Nonparametric
            ? "nonparametric"
            : "parametric";

        var result = _module.CreateSignificanceMatrix(
            (figSize.Width, figSize.Height),
            pyNumeric,
            pyCategorical,
            themeStr,
            dotSize,
            testFamilyStr);

        string svgContent = result.Item2;
        var pointDataList = result.Item3;

        var dataPoints = new List<SignificanceDataPoint>();
        foreach (var pointPyObj in pointDataList)
        {
            var d = pointPyObj.As<IReadOnlyDictionary<string, PyObject>>();
            // p_value arrives as NaN when the cell was untestable (<2 valid
            // groups / zero-variance group) — map that back to a null PValue.
            var pValueRaw = d["p_value"].As<double>();
            dataPoints.Add(new SignificanceDataPoint(
                CellRow: d["cell_row"].As<int>(),
                CellCol: d["cell_col"].As<int>(),
                X: d["x"].As<double>(),
                Y: d["y"].As<double>(),
                CategoricalColumn: d["cat_col"].As<string>(),
                NumericColumn: d["num_col"].As<string>(),
                Subgroup: d["subgroup"].As<string>(),
                Mean: d["mean"].As<double>(),
                Sem: d["sem"].As<double>(),
                N: d["n"].As<int>(),
                Color: d["color"].As<string>(),
                PValue: double.IsNaN(pValueRaw) ? null : pValueRaw,
                TestFamily: testFamily,
                Excluded: d["excluded"].As<bool>()));
        }

        return (svgContent, dataPoints);
    }
}
