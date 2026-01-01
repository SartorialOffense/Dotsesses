using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Dotsesses.Messages;
using Dotsesses.Models;
using Dotsesses.Services;

namespace Dotsesses.UI;

/// <summary>
/// ViewModel for the violin plot visualization.
/// </summary>
public partial class ViolinPlotViewModel : ViewModelBase
{
    private readonly ViolinPlotService _violinService;
    private readonly IMessenger _messenger;
    private readonly HoverDelayService _hoverDelayService;
    private List<ViolinDataPoint> _dataPoints = new();
    private double _svgWidth;
    private double _svgHeight;
    private double _displayWidth;
    private double _displayHeight;
    private List<(string SeriesName, Dictionary<string, double> Scores)> _seriesData = new();
    private Dictionary<(int StudentId, string SeriesName), string> _commentMap = new();
    private Dictionary<int, string> _muppetNameMap = new();
    private double _dotSize = 3.0;

    // Plot area bounds in SVG coordinates (extracted from data points)
    // In SVG, Y increases downward, so _svgPlotTop < _svgPlotBottom
    private double _svgPlotTop;    // SVG Y for normalized 1.0 (top of plot)
    private double _svgPlotBottom; // SVG Y for normalized 0.0 (bottom of plot)

    [ObservableProperty]
    private string? _svgContent;

    [ObservableProperty]
    private int? _hoveredStudentId;

    [ObservableProperty]
    private ObservableCollection<CursorViewModel>? _cursors;

    [ObservableProperty]
    private ObservableCollection<ComplianceRowViewModel>? _complianceRows;

    [ObservableProperty]
    private int _minScore;

    [ObservableProperty]
    private int _maxScore;


    public ViolinPlotViewModel(ViolinPlotService violinService, IMessenger messenger, HoverDelayService hoverDelayService)
    {
        try
        {
            var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dotsesses_startup.log");
            System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] ViolinPlotViewModel: Constructor started\n");
        }
        catch { }

        _violinService = violinService;
        _messenger = messenger;
        _hoverDelayService = hoverDelayService;

        // Subscribe to hover activation from delay service
        _hoverDelayService.OnHoverActivated += OnHoverActivated;

        // Register for hover messages from dotplot (for cross-view sync)
        _messenger.Register<StudentHoverMessage>(this, (r, m) =>
        {
            // Always respond to clear messages (null), only filter non-self sources for hover
            if (m.StudentId == null || m.Source != "violin")
            {
                HoveredStudentId = m.StudentId;
            }
        });

        try
        {
            var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dotsesses_startup.log");
            System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] ViolinPlotViewModel: Constructor completed\n");
        }
        catch { }
    }

    /// <summary>
    /// Called by HoverDelayService when a hover is activated (after delay and stability check).
    /// </summary>
    private void OnHoverActivated(int? studentId)
    {
        HoveredStudentId = studentId;

        // Broadcast to dotplot for cross-view sync
        _messenger.Send(new StudentHoverMessage(
            studentId,
            "violin",
            null));
    }

    /// <summary>
    /// Generates the violin plot with the given data.
    /// </summary>
    public void GeneratePlot(
        (double Width, double Height) displaySize,
        List<(string SeriesName, Dictionary<string, double> Scores)> seriesData,
        Dictionary<(int StudentId, string SeriesName), string> commentMap,
        Dictionary<int, string> muppetNameMap,
        double dotSize = 5.0)
    {
        // Store data for later regeneration
        _seriesData = seriesData;
        _commentMap = commentMap;
        _muppetNameMap = muppetNameMap;
        _dotSize = dotSize;
        _displayWidth = displaySize.Width;
        _displayHeight = displaySize.Height;

        // Calculate figure size in inches (DPI = 100)
        const double DPI = 100.0;
        double widthInches = displaySize.Width / DPI;
        double heightInches = displaySize.Height / DPI;

        // Generate plot via Python
        var (svgContent, dataPoints) = _violinService.GeneratePlot(
            (widthInches, heightInches),
            seriesData,
            commentMap,
            muppetNameMap,
            dotSize);

        SvgContent = svgContent;
        _dataPoints = dataPoints;

        // Extract actual SVG dimensions from viewBox
        ExtractSvgDimensions(svgContent);

        // Extract plot area Y bounds from data points
        ExtractPlotAreaBounds(dataPoints);
    }

    /// <summary>
    /// Regenerates the plot with new display dimensions using stored data.
    /// </summary>
    public void RegeneratePlot(double displayWidth, double displayHeight)
    {
        Console.WriteLine($"[ViolinPlotViewModel] RegeneratePlot called: {displayWidth}x{displayHeight}, SeriesData count: {_seriesData.Count}");

        if (_seriesData.Count == 0)
        {
            Console.WriteLine("[ViolinPlotViewModel] No series data to regenerate");
            return;
        }

        GeneratePlot((displayWidth, displayHeight), _seriesData, _commentMap, _muppetNameMap, _dotSize);
    }

    /// <summary>
    /// Updates the stored data and regenerates the plot with current display dimensions.
    /// </summary>
    public void UpdateDataAndRegenerate(
        List<(string SeriesName, Dictionary<string, double> Scores)> seriesData,
        Dictionary<(int StudentId, string SeriesName), string> commentMap,
        Dictionary<int, string> muppetNameMap,
        double dotSize)
    {
        // Store new data
        _seriesData = seriesData;
        _commentMap = commentMap;
        _muppetNameMap = muppetNameMap;
        _dotSize = dotSize;

        // Regenerate with stored display dimensions (or defaults if not yet set)
        var displayWidth = _displayWidth > 0 ? _displayWidth : 800;
        var displayHeight = _displayHeight > 0 ? _displayHeight : 400;

        GeneratePlot((displayWidth, displayHeight), _seriesData, _commentMap, _muppetNameMap, _dotSize);
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

        // Report hover candidate to delay service (it handles timing)
        if (candidateId != null)
        {
            _hoverDelayService.ReportHoverCandidate(candidateId, position);
        }
        // Don't clear on null - require explicit clear
    }

    /// <summary>
    /// Gets all data points for a specific student (across all series).
    /// </summary>
    public List<ViolinDataPoint> GetPointsForStudent(int studentId)
    {
        return _dataPoints.Where(p => p.StudentId == studentId).ToList();
    }

    /// <summary>
    /// Gets all data points.
    /// </summary>
    public List<ViolinDataPoint> GetAllPoints()
    {
        return _dataPoints;
    }

    /// <summary>
    /// Updates a comment for a specific student and series.
    /// This is an efficient update that doesn't regenerate the entire plot.
    /// </summary>
    public void UpdateComment(int studentId, string seriesName, string? comment)
    {
        var key = (studentId, seriesName);
        var normalizedComment = comment ?? "";

        // Update the comment map
        _commentMap[key] = normalizedComment;

        // Find and replace the data point with the updated comment
        for (int i = 0; i < _dataPoints.Count; i++)
        {
            var point = _dataPoints[i];
            if (point.StudentId == studentId && point.Series == seriesName)
            {
                // Create new record with updated comment
                _dataPoints[i] = point with { Comment = normalizedComment };
                break;
            }
        }

        // Trigger re-render of hover visualization if this student is currently hovered
        if (HoveredStudentId == studentId)
        {
            // Force property change notification by toggling the value
            var temp = HoveredStudentId;
            HoveredStudentId = null;
            HoveredStudentId = temp;
        }
    }

    /// <summary>
    /// Gets the X bounds (in display coordinates) for the "Total" series.
    /// Calculates series width based on plot width divided by number of series,
    /// accounting for extra right-side padding in the plot.
    /// Returns null if no Total series found.
    /// </summary>
    public (double Left, double Right)? GetTotalSeriesDisplayBounds(double displayWidth, double displayHeight)
    {
        // Find all points in the Total series
        var totalPoints = _dataPoints.Where(p =>
            p.Series.Equals("Total", StringComparison.OrdinalIgnoreCase)).ToList();

        if (!totalPoints.Any())
            return null;

        // Get the number of unique series
        var seriesNames = _dataPoints.Select(p => p.Series).Distinct().ToList();
        var numSeries = seriesNames.Count;
        if (numSeries == 0) return null;

        // Calculate the center X of the Total series in SVG coordinates
        var totalCenterSvgX = totalPoints.Average(p => p.X);

        // Convert to display coordinates
        var (centerDisplay, _) = SvgToDisplayWithSize(totalCenterSvgX, 0, displayWidth, displayHeight);

        // The plot has extra padding on the right for statistics labels (approximately 0.25 units in matplotlib)
        // We need to subtract this from the effective plot width before dividing by series count
        // The padding takes up roughly the equivalent of 0.25/numSeries of the total width
        const double rightPaddingFraction = 0.25;  // matches xlim adjustment in Python
        var effectivePlotWidth = displayWidth * (numSeries / (numSeries + rightPaddingFraction));

        // Calculate estimated series width
        var seriesWidth = effectivePlotWidth / numSeries;

        // Center the barbell bounds around the Total series center
        var halfWidth = seriesWidth / 2.0;
        var left = centerDisplay - halfWidth;
        var right = centerDisplay + halfWidth;

        return (left, right);
    }

    /// <summary>
    /// Converts SVG coordinates to display coordinates using stored display size.
    /// </summary>
    public (double X, double Y) SvgToDisplay(double svgX, double svgY)
    {
        return SvgToDisplayWithSize(svgX, svgY, _displayWidth, _displayHeight);
    }

    /// <summary>
    /// Converts SVG coordinates to display coordinates using specified display size.
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
        // Parse viewBox="0 0 width height" from SVG
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
            // Fallback to approximate dimensions if parsing fails
            const double DPI = 100.0;
            _svgWidth = _displayWidth / DPI * 72;
            _svgHeight = _displayHeight / DPI * 72;
        }
    }

    /// <summary>
    /// Extracts the plot area Y bounds from data points.
    /// The data points have Y coordinates in SVG space, and their normalized values
    /// tell us which Y corresponds to 0.0 and which to 1.0 in the plot.
    /// </summary>
    private void ExtractPlotAreaBounds(List<ViolinDataPoint> dataPoints)
    {
        if (dataPoints.Count == 0)
        {
            // Fallback to full SVG height
            _svgPlotTop = 0;
            _svgPlotBottom = _svgHeight;
            return;
        }

        // Find points with min and max normalized values to determine Y bounds
        // The Value field is the raw score - we need to find the actual SVG Y
        // for the highest and lowest normalized scores
        var minY = dataPoints.Min(p => p.Y);  // Top of plot in SVG (lowest Y)
        var maxY = dataPoints.Max(p => p.Y);  // Bottom of plot in SVG (highest Y)

        // In SVG coordinates, Y increases downward
        // High normalized values (1.0) → low SVG Y (top)
        // Low normalized values (0.0) → high SVG Y (bottom)
        _svgPlotTop = minY;
        _svgPlotBottom = maxY;

        Console.WriteLine($"[ViolinPlotViewModel] Plot area bounds: top={_svgPlotTop:F1}, bottom={_svgPlotBottom:F1}");
    }

    /// <summary>
    /// Gets the plot area top position as a fraction of display height.
    /// </summary>
    public double GetPlotAreaTopFraction()
    {
        if (_svgHeight == 0) return 0;
        return _svgPlotTop / _svgHeight;
    }

    /// <summary>
    /// Gets the plot area bottom position as a fraction of display height.
    /// </summary>
    public double GetPlotAreaBottomFraction()
    {
        if (_svgHeight == 0) return 1;
        return _svgPlotBottom / _svgHeight;
    }

    /// <summary>
    /// Converts a raw score value to normalized 0-1 scale (matching violin plot Y-axis).
    /// High scores → 1.0, low scores → 0.0.
    /// </summary>
    public double ScoreToNormalized(int score)
    {
        if (MaxScore == MinScore) return 0.5;
        return (double)(score - MinScore) / (MaxScore - MinScore);
    }

    /// <summary>
    /// Converts a normalized 0-1 value back to raw score.
    /// </summary>
    public int NormalizedToScore(double normalized)
    {
        return (int)Math.Round(MinScore + normalized * (MaxScore - MinScore));
    }

    /// <summary>
    /// Converts a score value to display Y coordinate (for Canvas region bands).
    /// Maps to the plot area within the display, accounting for SVG margins.
    /// High scores at top, low scores at bottom.
    /// </summary>
    public double ScoreToDisplayY(int score, double displayHeight)
    {
        if (MaxScore == MinScore) return displayHeight / 2;

        // Get the plot area bounds as fractions of total height
        var topFraction = GetPlotAreaTopFraction();
        var bottomFraction = GetPlotAreaBottomFraction();

        // Calculate display Y coordinates for plot area
        var plotAreaTop = topFraction * displayHeight;
        var plotAreaBottom = bottomFraction * displayHeight;
        var plotAreaHeight = plotAreaBottom - plotAreaTop;

        if (plotAreaHeight <= 0) return displayHeight / 2;

        // Map normalized score to plot area
        // normalized 1.0 → plotAreaTop, normalized 0.0 → plotAreaBottom
        var normalized = ScoreToNormalized(score);
        return plotAreaTop + (1.0 - normalized) * plotAreaHeight;
    }

    /// <summary>
    /// Converts a normalized Y value (0-1 range, can extend beyond) to display Y coordinate.
    /// normalized 1.0 → plotAreaTop, normalized 0.0 → plotAreaBottom
    /// </summary>
    public double NormalizedYToDisplayY(double normalizedY, double displayHeight)
    {
        // Get the plot area bounds as fractions of total height
        var topFraction = GetPlotAreaTopFraction();
        var bottomFraction = GetPlotAreaBottomFraction();

        // Calculate display Y coordinates for plot area
        var plotAreaTop = topFraction * displayHeight;
        var plotAreaBottom = bottomFraction * displayHeight;
        var plotAreaHeight = plotAreaBottom - plotAreaTop;

        if (plotAreaHeight <= 0) return displayHeight / 2;

        // Map normalized Y to plot area
        // normalized 1.0 → plotAreaTop, normalized 0.0 → plotAreaBottom
        // Values below 0 (like -0.2) will be below plotAreaBottom
        return plotAreaTop + (1.0 - normalizedY) * plotAreaHeight;
    }
}
