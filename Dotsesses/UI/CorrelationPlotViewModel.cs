using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Dotsesses.Messages;
using Dotsesses.Models;
using Dotsesses.Services;

namespace Dotsesses.UI;

/// <summary>
/// ViewModel for the correlation matrix plot visualization.
/// </summary>
public partial class CorrelationPlotViewModel : ViewModelBase
{
    private readonly CorrelationPlotService _correlationService;
    private readonly IMessenger _messenger;
    private readonly HoverDelayService _hoverDelayService;
    private List<CorrelationDataPoint> _dataPoints = new();
    private double _svgWidth;
    private double _svgHeight;
    private double _displayWidth;
    private double _displayHeight;
    private List<(string SeriesName, Dictionary<string, double> Scores)> _seriesData = new();
    private Dictionary<string, CorrelationColumnInfo> _columnMetadata = new();
    private Dictionary<int, string> _muppetNameMap = new();
    private double _dotSize = 3.0;
    private int _seriesCount;

    [ObservableProperty]
    private string? _svgContent;

    [ObservableProperty]
    private int? _hoveredStudentId;

    [ObservableProperty]
    private string _diagonalType = "kde"; // "kde" or "hist"

    [ObservableProperty]
    private bool _showCorrelationCoefficients = true;

    /// <summary>
    /// The scoped messenger for this VM's workspace. Exposed for the
    /// View code-behind (CorrelationPlotControl) to register/send within
    /// the same window's scope. See ADR-0012.
    /// </summary>
    public IMessenger Messenger => _messenger;

    public CorrelationPlotViewModel(
        CorrelationPlotService correlationService,
        IMessenger messenger,
        HoverDelayService hoverDelayService)
    {
        _correlationService = correlationService;
        _messenger = messenger;
        _hoverDelayService = hoverDelayService;

        // Subscribe to hover activation from delay service
        _hoverDelayService.OnHoverActivated += OnHoverActivated;

        // Register for hover messages from other views (for cross-view sync)
        _messenger.Register<StudentHoverMessage>(this, (r, m) =>
        {
            // Always respond to clear messages (null), only filter non-self sources for hover
            if (m.StudentId == null || m.Source != "correlation")
            {
                HoveredStudentId = m.StudentId;
            }
        });
    }

    /// <summary>
    /// Called by HoverDelayService when a hover is activated.
    /// </summary>
    private void OnHoverActivated(int? studentId)
    {
        HoveredStudentId = studentId;

        // Broadcast to other views for cross-view sync
        _messenger.Send(new StudentHoverMessage(
            studentId,
            "correlation",
            null));
    }

    /// <summary>
    /// Generates the correlation matrix plot with the given data.
    /// </summary>
    public void GeneratePlot(
        (double Width, double Height) displaySize,
        List<(string SeriesName, Dictionary<string, double> Scores)> seriesData,
        Dictionary<string, CorrelationColumnInfo> columnMetadata,
        Dictionary<int, string> muppetNameMap,
        double dotSize = 3.0,
        ThemeName theme = ThemeName.DarkMode)
    {
        // Store data for later regeneration
        _seriesData = seriesData;
        _columnMetadata = columnMetadata;
        _muppetNameMap = muppetNameMap;
        _dotSize = dotSize;
        _displayWidth = displaySize.Width;
        _displayHeight = displaySize.Height;
        _seriesCount = seriesData.Count;

        // Calculate figure size in inches (DPI = 100)
        const double DPI = 100.0;
        double widthInches = displaySize.Width / DPI;
        double heightInches = displaySize.Height / DPI;

        // Generate plot via Python
        var (svgContent, dataPoints) = _correlationService.GeneratePlot(
            (widthInches, heightInches),
            seriesData,
            columnMetadata,
            muppetNameMap,
            dotSize,
            ShowCorrelationCoefficients,
            DiagonalType,
            theme);

        SvgContent = svgContent;
        _dataPoints = dataPoints;

        // Extract actual SVG dimensions from viewBox
        ExtractSvgDimensions(svgContent);
    }

    /// <summary>
    /// Regenerates the plot with new display dimensions using stored data.
    /// </summary>
    public void RegeneratePlot(double displayWidth, double displayHeight, ThemeName theme = ThemeName.DarkMode)
    {
        if (_seriesData.Count == 0)
            return;

        GeneratePlot((displayWidth, displayHeight), _seriesData, _columnMetadata, _muppetNameMap, _dotSize, theme);
    }

    /// <summary>
    /// Updates the stored data and regenerates the plot.
    /// </summary>
    public void UpdateDataAndRegenerate(
        List<(string SeriesName, Dictionary<string, double> Scores)> seriesData,
        Dictionary<string, CorrelationColumnInfo> columnMetadata,
        Dictionary<int, string> muppetNameMap,
        double dotSize)
    {
        _seriesData = seriesData;
        _columnMetadata = columnMetadata;
        _muppetNameMap = muppetNameMap;
        _dotSize = dotSize;

        var displayWidth = _displayWidth > 0 ? _displayWidth : 800;
        var displayHeight = _displayHeight > 0 ? _displayHeight : 600;

        GeneratePlot((displayWidth, displayHeight), _seriesData, _columnMetadata, _muppetNameMap, _dotSize);
    }

    /// <summary>
    /// Handles pointer moved event for hover detection.
    /// </summary>
    public void OnPointerMoved(Point position, double displayWidth, double displayHeight)
    {
        if (_dataPoints.Count == 0 || displayWidth == 0 || displayHeight == 0)
            return;

        // Calculate scale factors for SVG to display conversion
        double scaleX = displayWidth / _svgWidth;
        double scaleY = displayHeight / _svgHeight;

        // Find closest student within 15px tolerance
        var hit = _dataPoints
            .Select(p => new
            {
                Point = p,
                DisplayX = p.X * scaleX,
                DisplayY = p.Y * scaleY,
                Dist = Math.Sqrt(Math.Pow(position.X - p.X * scaleX, 2) +
                                  Math.Pow(position.Y - p.Y * scaleY, 2))
            })
            .OrderBy(x => x.Dist)
            .FirstOrDefault();

        int? candidateId = hit != null && hit.Dist < 15 ? hit.Point.StudentId : null;

        // Report hover candidate to delay service
        _hoverDelayService.ReportHoverCandidate(candidateId, position);
    }

    /// <summary>
    /// Gets all data points for a specific student (across all cells).
    /// </summary>
    public List<CorrelationDataPoint> GetPointsForStudent(int studentId)
    {
        return _dataPoints.Where(p => p.StudentId == studentId).ToList();
    }

    /// <summary>
    /// Gets all data points.
    /// </summary>
    public List<CorrelationDataPoint> GetAllPoints()
    {
        return _dataPoints;
    }

    /// <summary>
    /// Builds the hover tooltip text for a correlation cell from one of its
    /// data points (ADR-0018 slice 3). Surfaces the dot's own x/y values — with
    /// the rest-score subtraction spelled out for a corrected dot — then the
    /// effect-size headline (r²), the coefficient + method (Pearson r /
    /// Spearman ρ), the raw p and N as supporting detail, and a "corrected" note
    /// when the cell used the rest-score de-bias. Pure/static so the formatting
    /// is unit-testable independent of the Avalonia control that attaches it.
    /// </summary>
    public static string BuildPointTooltip(CorrelationDataPoint p)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var symbol = p.Method == "spearman" ? "ρ" : "r";
        var methodName = p.Method == "spearman" ? "Spearman" : "Pearson";
        var rSquared = p.R * p.R;

        string pText;
        if (p.PValue is not double pv)
        {
            pText = "p —";
        }
        else if (pv < 0.001)
        {
            pText = "p<.001";
        }
        else
        {
            // ".034" style — strip the leading zero, matching the in-cell stars.
            pText = "p=" + pv.ToString("0.000", ci).TrimStart('0');
        }

        var stars = SignificanceStars(p.PValue);

        // Per-dot values (ADR-0018 slice 3 follow-up). The x value is always
        // raw (x is never transformed); the y line shows the de-bias subtraction
        // explicitly for a corrected dot — target − column = raw − column = rest
        // — so you can see the de-bias actually happened. A non-corrected dot
        // just shows its raw y. "0.##" drops trailing zeros (85, 12.5).
        var xLine = $"{p.XSeries} = {p.XValue.ToString("0.##", ci)}";
        var yLine = p.Corrected
            ? $"{p.YSeries} − {p.XSeries} = {p.YRaw.ToString("0.##", ci)} − " +
              $"{p.XValue.ToString("0.##", ci)} = {p.YValue.ToString("0.##", ci)}"
            : $"{p.YSeries} = {p.YValue.ToString("0.##", ci)}";

        var lines = new List<string>
        {
            $"{p.XSeries} × {p.YSeries}",
            xLine,
            yLine,
            $"r²={rSquared.ToString("0.00", ci)}  ({methodName} {symbol}={p.R.ToString("0.00", ci)})",
            $"{pText}{(stars.Length > 0 ? " " + stars : "")}   N={p.N}",
        };
        if (p.Corrected)
        {
            lines.Add("corrected (rest score)");
        }
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Star tiers for a raw p-value, mirroring the Python <c>significance_stars</c>
    /// helper (ADR-0018 decision 4). Kept in sync for the C#-built tooltip text.
    /// </summary>
    private static string SignificanceStars(double? p)
    {
        if (p is not double v || double.IsNaN(v)) return "";
        if (v < 0.001) return "***";
        if (v < 0.01) return "**";
        if (v < 0.05) return "*";
        return "";
    }

    /// <summary>
    /// Gets the number of series in the current plot.
    /// </summary>
    public int SeriesCount => _seriesCount;

    /// <summary>
    /// Converts SVG coordinates to display coordinates.
    /// </summary>
    public (double X, double Y) SvgToDisplayWithSize(double svgX, double svgY, double displayWidth, double displayHeight)
    {
        if (_svgWidth == 0 || _svgHeight == 0)
            return (0, 0);

        double scaleX = displayWidth / _svgWidth;
        double scaleY = displayHeight / _svgHeight;

        return (svgX * scaleX, svgY * scaleY);
    }

    /// <summary>
    /// Extracts actual SVG dimensions from viewBox attribute.
    /// </summary>
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
    /// Triggers a re-render of the hover visualization.
    /// </summary>
    public void RefreshHoverVisualization()
    {
        OnPropertyChanged(nameof(HoveredStudentId));
    }

    /// <summary>
    /// Toggles the diagonal type between KDE and histogram.
    /// </summary>
    public void ToggleDiagonalType()
    {
        DiagonalType = DiagonalType == "kde" ? "hist" : "kde";

        // Regenerate plot with new diagonal type
        if (_seriesData.Count > 0 && _displayWidth > 0 && _displayHeight > 0)
        {
            RegeneratePlot(_displayWidth, _displayHeight);
        }
    }
}
