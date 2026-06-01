using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Dotsesses.Models;
using Dotsesses.Services;

namespace Dotsesses.UI;

/// <summary>
/// ViewModel for the Significance Matrix plot — a matrix of small scatter
/// cells with one row per Numeric column and one column per Categorical
/// column. Each cell plots one dot per Subgroup of the column's categorical
/// column at (subgroup index, mean of numeric value) with a ±SEM error bar.
///
/// Unlike <see cref="CorrelationPlotViewModel"/>, this VM has **no cross-view
/// sync**: the dots represent subgroups (aggregates), not individual
/// students, so hovering a dot here doesn't highlight anything in the
/// dotplot/violin, and vice versa. The tooltip on each dot exposes the
/// subgroup name, mean, SEM, and N — that's the entire interaction model
/// in slice 3. Future slice 4 will add a per-cell p-value annotation
/// (see ADR-0014).
/// </summary>
public partial class SignificancePlotViewModel : ViewModelBase
{
    private readonly SignificancePlotService _service;
    private readonly IMessenger _messenger;
    private List<SignificanceDataPoint> _dataPoints = new();
    private double _svgWidth;
    private double _svgHeight;
    private double _displayWidth;
    private double _displayHeight;
    private List<(string Name, Dictionary<string, double> Values)> _numericSeries = new();
    private List<(string Name, Dictionary<string, string> Subgroups)> _categoricalSeries = new();
    private List<(string Name, List<string> Order)> _subgroupOrders = new();
    private double _dotSize = 5.0;

    [ObservableProperty]
    private string? _svgContent;

    /// <summary>
    /// The omnibus test family applied per cell (see ADR-0014 slice 4).
    /// Bound to the top-of-plot radio; the control regenerates on change.
    /// Persisted with the workspace (SavedState v5).
    /// </summary>
    [ObservableProperty]
    private SignificanceTestFamily _testFamily = SignificanceTestFamily.Parametric;

    /// <summary>
    /// Whether each cell overlays a mean ± 95% CI marker on top of the box +
    /// points (never SEM — ADR-0019). Session-only view preference (NOT
    /// persisted, mirroring the Correlation diagonal toggle); the control
    /// regenerates on change.
    /// </summary>
    [ObservableProperty]
    private bool _showConfidenceInterval;

    /// <summary>
    /// Scoped messenger for this VM's workspace (see ADR-0012). Exposed for
    /// the View code-behind even though Significance has no cross-view sync —
    /// keeping the symmetric shape lets future features (e.g. theme-render
    /// broadcast) reach this control without a refactor.
    /// </summary>
    public IMessenger Messenger => _messenger;

    /// <summary>
    /// Invoked when the user clicks Optimize. Wired by MainWindowViewModel
    /// (the single consumer that owns the selections — direct callback per
    /// ADR-0004). Re-picks the matrix's numeric rows to the most significant
    /// for the currently-displayed categoricals.
    /// </summary>
    public Action? OptimizeRequested { get; set; }

    /// <summary>Raised from the Optimize button in the View code-behind.</summary>
    public void RequestOptimize() => OptimizeRequested?.Invoke();

    public SignificancePlotViewModel(
        SignificancePlotService service,
        IMessenger messenger)
    {
        _service = service;
        _messenger = messenger;
    }

    public void GeneratePlot(
        (double Width, double Height) displaySize,
        List<(string Name, Dictionary<string, double> Values)> numericSeries,
        List<(string Name, Dictionary<string, string> Subgroups)> categoricalSeries,
        double dotSize = 5.0,
        ThemeName theme = ThemeName.DarkMode,
        List<(string Name, List<string> Order)>? subgroupOrders = null)
    {
        _numericSeries = numericSeries;
        _categoricalSeries = categoricalSeries;
        _subgroupOrders = subgroupOrders ?? new();
        _dotSize = dotSize;
        _displayWidth = displaySize.Width;
        _displayHeight = displaySize.Height;

        const double DPI = 100.0;
        double widthInches = displaySize.Width / DPI;
        double heightInches = displaySize.Height / DPI;

        var (svgContent, dataPoints) = _service.GeneratePlot(
            (widthInches, heightInches),
            numericSeries,
            categoricalSeries,
            dotSize,
            theme,
            TestFamily,
            _subgroupOrders,
            showCi: ShowConfidenceInterval);

        SvgContent = svgContent;
        _dataPoints = dataPoints;
        ExtractSvgDimensions(svgContent);
    }

    public void RegeneratePlot(double displayWidth, double displayHeight, ThemeName theme = ThemeName.DarkMode)
    {
        if (_numericSeries.Count == 0 && _categoricalSeries.Count == 0)
        {
            return;
        }
        GeneratePlot((displayWidth, displayHeight), _numericSeries, _categoricalSeries, _dotSize, theme, _subgroupOrders);
    }

    public void UpdateDataAndRegenerate(
        List<(string Name, Dictionary<string, double> Values)> numericSeries,
        List<(string Name, Dictionary<string, string> Subgroups)> categoricalSeries,
        double dotSize,
        List<(string Name, List<string> Order)>? subgroupOrders = null)
    {
        _numericSeries = numericSeries;
        _categoricalSeries = categoricalSeries;
        _subgroupOrders = subgroupOrders ?? new();
        _dotSize = dotSize;

        var displayWidth = _displayWidth > 0 ? _displayWidth : 800;
        var displayHeight = _displayHeight > 0 ? _displayHeight : 600;
        GeneratePlot((displayWidth, displayHeight), _numericSeries, _categoricalSeries, _dotSize, ThemeName.DarkMode, _subgroupOrders);
    }

    public List<SignificanceDataPoint> GetAllPoints() => _dataPoints;

    public (double X, double Y) SvgToDisplayWithSize(double svgX, double svgY, double displayWidth, double displayHeight)
    {
        if (_svgWidth == 0 || _svgHeight == 0) return (0, 0);
        double scaleX = displayWidth / _svgWidth;
        double scaleY = displayHeight / _svgHeight;
        return (svgX * scaleX, svgY * scaleY);
    }

    private void ExtractSvgDimensions(string svgContent)
    {
        var viewBoxMatch = System.Text.RegularExpressions.Regex.Match(
            svgContent,
            @"viewBox=""[\d\.\-\s]+\s+([\d\.]+)\s+([\d\.]+)""");

        if (viewBoxMatch.Success)
        {
            _svgWidth = double.Parse(viewBoxMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            _svgHeight = double.Parse(viewBoxMatch.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        }
        else
        {
            const double DPI = 100.0;
            _svgWidth = _displayWidth / DPI * 72;
            _svgHeight = _displayHeight / DPI * 72;
        }
    }

    /// <summary>
    /// Builds the per-dot tooltip text. N=1 collapses to mean only (SEM
    /// undefined). The cell's omnibus test result is appended (slice 4): the
    /// p-value + test name, or a note that the cell/subgroup couldn't be
    /// tested. The p-value is a cell property, so every dot in the cell shows
    /// the same value.
    /// </summary>
    public static string BuildTooltip(SignificanceDataPoint p)
    {
        // Per-student point (ADR-0019): show this student's score and the
        // subgroup context. The cell's spread/centre is read from the box; its
        // effect size + p live in the in-cell annotation.
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var head = $"{p.CategoricalColumn} = {p.Subgroup}   (N = {p.N})\n" +
                   $"{p.NumericColumn}: {p.Value.ToString("0.##", ci)}";

        var testName = p.TestFamily == SignificanceTestFamily.Nonparametric
            ? "Kruskal–Wallis"
            : "Welch ANOVA";
        // ε² accompanies Kruskal–Wallis, η² accompanies Welch ANOVA (ADR-0018).
        var effectSymbol = p.TestFamily == SignificanceTestFamily.Nonparametric
            ? "ε²"
            : "η²";

        string statLine;
        if (p.Excluded)
        {
            statLine = $"excluded from test: N < 2";
        }
        else if (p.PValue is null)
        {
            statLine = $"{testName}: not testable (< 2 groups with N ≥ 2)";
        }
        else
        {
            // Effect-size-led (ADR-0018): the variance-explained headline first,
            // raw p + test demoted to a supporting line beneath.
            var headline = p.EffectSize is double e
                ? $"{effectSymbol}={e.ToString("0.00", ci).TrimStart('0')} variance explained"
                : $"{effectSymbol}=—";
            var stars = SignificanceStars(p.PValue);
            var pText = "p=" + p.PValue.Value.ToString("0.###", ci).TrimStart('0');
            statLine = $"{headline}\n{pText}{(stars.Length > 0 ? " " + stars : "")} · {testName}";
        }

        return head + "\n" + statLine;
    }

    /// <summary>
    /// Star tiers for a raw p-value (ADR-0018 decision 4) — mirrors the Python
    /// <c>significance_stars</c> helper for the C#-built tooltip text.
    /// </summary>
    private static string SignificanceStars(double? p)
    {
        if (p is not double v || double.IsNaN(v)) return "";
        if (v < 0.001) return "***";
        if (v < 0.01) return "**";
        if (v < 0.05) return "*";
        return "";
    }
}
