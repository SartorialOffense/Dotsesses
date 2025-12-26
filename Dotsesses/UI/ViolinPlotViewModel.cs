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
    private Dictionary<int, string> _commentMap = new();
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
            if (m.Source != "violin") // Only respond to dotplot messages
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
        Dictionary<int, string> commentMap,
        double dotSize = 5.0)
    {
        // Store data for later regeneration
        _seriesData = seriesData;
        _commentMap = commentMap;
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

        GeneratePlot((displayWidth, displayHeight), _seriesData, _commentMap, _dotSize);
    }

    /// <summary>
    /// Updates the stored data and regenerates the plot with current display dimensions.
    /// </summary>
    public void UpdateDataAndRegenerate(
        List<(string SeriesName, Dictionary<string, double> Scores)> seriesData,
        Dictionary<int, string> commentMap,
        double dotSize)
    {
        // Store new data
        _seriesData = seriesData;
        _commentMap = commentMap;
        _dotSize = dotSize;

        // Regenerate with stored display dimensions (or defaults if not yet set)
        var displayWidth = _displayWidth > 0 ? _displayWidth : 800;
        var displayHeight = _displayHeight > 0 ? _displayHeight : 400;

        GeneratePlot((displayWidth, displayHeight), _seriesData, _commentMap, _dotSize);
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
    /// Gets the X bounds (in display coordinates) for the "Total" series.
    /// Left edge is midpoint between rightmost dot of adjacent series and leftmost Total dot.
    /// Right edge uses symmetric spacing from rightmost Total dot, with margin from plot edge.
    /// Returns null if no Total series found.
    /// </summary>
    public (double Left, double Right)? GetTotalSeriesDisplayBounds(double displayWidth, double displayHeight)
    {
        // Find all points in the Total series
        var totalPoints = _dataPoints.Where(p =>
            p.Series.Equals("Total", StringComparison.OrdinalIgnoreCase)).ToList();

        if (!totalPoints.Any())
            return null;

        // Get leftmost and rightmost Total point X in SVG coordinates
        var totalLeftSvgX = totalPoints.Min(p => p.X);
        var totalRightSvgX = totalPoints.Max(p => p.X);

        // Find the series directly to the left of Total (has the highest X that's less than Total's min X)
        var adjacentSeriesPoints = _dataPoints
            .Where(p => !p.Series.Equals("Total", StringComparison.OrdinalIgnoreCase))
            .GroupBy(p => p.Series)
            .Select(g => new { Series = g.Key, MaxX = g.Max(p => p.X) })
            .Where(s => s.MaxX < totalLeftSvgX)
            .OrderByDescending(s => s.MaxX)
            .FirstOrDefault();

        double leftBoundSvgX;
        double halfGap;
        if (adjacentSeriesPoints != null)
        {
            // Midpoint between rightmost dot of adjacent series and leftmost Total dot
            halfGap = (totalLeftSvgX - adjacentSeriesPoints.MaxX) / 2.0;
            leftBoundSvgX = adjacentSeriesPoints.MaxX + halfGap;
        }
        else
        {
            // Fallback: use Total's left edge with some padding
            halfGap = 10;
            leftBoundSvgX = totalLeftSvgX - halfGap;
        }

        // Right edge: symmetric spacing from rightmost Total dot
        var rightBoundSvgX = totalRightSvgX + halfGap;

        // Convert to display coordinates
        var (left, _) = SvgToDisplayWithSize(leftBoundSvgX, 0, displayWidth, displayHeight);
        var (right, _) = SvgToDisplayWithSize(rightBoundSvgX, 0, displayWidth, displayHeight);

        // Ensure right edge doesn't exceed display width minus margin
        const double rightMargin = 20;
        if (right > displayWidth - rightMargin)
        {
            // Clamp right edge and adjust left to maintain symmetry around Total center
            var totalCenterSvgX = (totalLeftSvgX + totalRightSvgX) / 2.0;
            var (centerDisplay, _) = SvgToDisplayWithSize(totalCenterSvgX, 0, displayWidth, displayHeight);

            right = displayWidth - rightMargin;
            var halfWidth = right - centerDisplay;
            left = centerDisplay - halfWidth;
        }

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
}
