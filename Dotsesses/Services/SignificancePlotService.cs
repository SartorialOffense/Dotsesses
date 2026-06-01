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
        SignificanceTestFamily testFamily = SignificanceTestFamily.Parametric,
        List<(string Name, List<string> Order)>? subgroupOrders = null)
    {
        var pyNumeric = numericSeries
            .Select(s => (s.Name, (IReadOnlyDictionary<string, double>)s.Values.AsReadOnly()))
            .ToList();
        var pyCategorical = categoricalSeries
            .Select(s => (s.Name, (IReadOnlyDictionary<string, string>)s.Subgroups.AsReadOnly()))
            .ToList();

        // Canonical subgroup order per categorical column (ADR-0017). Built in
        // C#; Python consumes it verbatim. Look up by series name and emit a
        // list aligned with pyCategorical so column j matches order j; a column
        // with no supplied order gets an empty list (Python falls back to alpha).
        var orderByName = (subgroupOrders ?? new())
            .GroupBy(o => o.Name)
            .ToDictionary(g => g.Key, g => g.First().Order);
        var pyOrdered = categoricalSeries
            .Select(s => (IReadOnlyList<string>)(
                orderByName.TryGetValue(s.Name, out var o) ? o : new List<string>()))
            .ToList();

        var themeStr = theme == ThemeName.LightMode ? "light" : "dark";
        var testFamilyStr = testFamily == SignificanceTestFamily.Nonparametric
            ? "nonparametric"
            : "parametric";

        var result = _module.CreateSignificanceMatrix(
            (figSize.Width, figSize.Height),
            pyNumeric,
            pyCategorical,
            pyOrdered,
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
            // effect_size arrives as NaN when the cell was untestable (ADR-0018).
            var effectRaw = d["effect_size"].As<double>();
            // Student id arrives as "S001"; parse to int like the correlation path.
            var idStr = d["student_id"].As<string>();
            int studentId = int.Parse(idStr.TrimStart('S'));
            dataPoints.Add(new SignificanceDataPoint(
                CellRow: d["cell_row"].As<int>(),
                CellCol: d["cell_col"].As<int>(),
                X: d["x"].As<double>(),
                Y: d["y"].As<double>(),
                CategoricalColumn: d["cat_col"].As<string>(),
                NumericColumn: d["num_col"].As<string>(),
                Subgroup: d["subgroup"].As<string>(),
                StudentId: studentId,
                Value: d["value"].As<double>(),
                N: d["n"].As<int>(),
                Color: d["color"].As<string>(),
                PValue: double.IsNaN(pValueRaw) ? null : pValueRaw,
                EffectSize: double.IsNaN(effectRaw) ? null : effectRaw,
                TestFamily: testFamily,
                Excluded: d["excluded"].As<bool>()));
        }

        return (svgContent, dataPoints);
    }

    /// <summary>
    /// Computes the per-cell omnibus p-value for every (numeric, categorical)
    /// pair in a single Python call — no plot rendering. Used at load time to
    /// drive the data-driven default Significance selection (see ADR-0016).
    /// Returns a map keyed by (numericSeriesName, categoricalSeriesName); a
    /// null value means the cell was untestable.
    /// </summary>
    public Dictionary<(string Num, string Cat), double?> ComputeCellPValues(
        List<(string Name, Dictionary<string, double> Values)> numericSeries,
        List<(string Name, Dictionary<string, string> Subgroups)> categoricalSeries,
        SignificanceTestFamily testFamily = SignificanceTestFamily.Parametric)
    {
        var result = new Dictionary<(string Num, string Cat), double?>();
        if (numericSeries.Count == 0 || categoricalSeries.Count == 0)
        {
            return result;
        }

        var pyNumeric = numericSeries
            .Select(s => (s.Name, (IReadOnlyDictionary<string, double>)s.Values.AsReadOnly()))
            .ToList();
        var pyCategorical = categoricalSeries
            .Select(s => (s.Name, (IReadOnlyDictionary<string, string>)s.Subgroups.AsReadOnly()))
            .ToList();

        var testFamilyStr = testFamily == SignificanceTestFamily.Nonparametric
            ? "nonparametric"
            : "parametric";

        var rows = _module.ComputeSignificancePvalues(pyNumeric, pyCategorical, testFamilyStr);
        foreach (var rowObj in rows)
        {
            var d = rowObj.As<IReadOnlyDictionary<string, PyObject>>();
            var p = d["p"].As<double>();
            result[(d["num"].As<string>(), d["cat"].As<string>())] =
                double.IsNaN(p) ? null : p;
        }
        return result;
    }
}
