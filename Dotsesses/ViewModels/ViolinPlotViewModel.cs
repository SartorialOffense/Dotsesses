using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Dotsesses.Messages;
using Dotsesses.Models;
using Dotsesses.Services;

namespace Dotsesses.ViewModels;

/// <summary>
/// ViewModel for the violin plot visualization.
/// </summary>
public partial class ViolinPlotViewModel : ViewModelBase
{
    private readonly ViolinPlotService _violinService;
    private readonly IMessenger _messenger;
    private List<ViolinDataPoint> _dataPoints = new();
    private double _svgWidth;
    private double _svgHeight;
    private double _displayWidth;
    private double _displayHeight;
    private List<(string SeriesName, Dictionary<string, double> Scores)> _seriesData = new();
    private double _dotSize = 3.0;

    [ObservableProperty]
    private string? _svgContent;

    [ObservableProperty]
    private int? _hoveredStudentId;

    public ViolinPlotViewModel(ViolinPlotService violinService, IMessenger messenger)
    {
        _violinService = violinService;
        _messenger = messenger;

        // Register for hover messages from dotplot
        _messenger.Register<StudentHoverMessage>(this, (r, m) =>
        {
            if (m.Source != "violin") // Only respond to dotplot messages
            {
                HoveredStudentId = m.StudentId;
            }
        });
    }

    /// <summary>
    /// Generates the violin plot with the given data.
    /// </summary>
    public void GeneratePlot(
        (double Width, double Height) displaySize,
        List<(string SeriesName, Dictionary<string, double> Scores)> seriesData,
        double dotSize = 5.0)
    {
        // Store data for later regeneration
        _seriesData = seriesData;
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
            dotSize);

        SvgContent = svgContent;
        _dataPoints = dataPoints;

        // Extract actual SVG dimensions from viewBox
        ExtractSvgDimensions(svgContent);
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

        GeneratePlot((displayWidth, displayHeight), _seriesData, _dotSize);
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

        int? newHoveredId = hit != null && hit.Dist < 15 ? hit.Point.StudentId : null;

        if (newHoveredId != HoveredStudentId)
        {
            HoveredStudentId = newHoveredId;

            // Broadcast hover message to dotplot
            _messenger.Send(new StudentHoverMessage(
                HoveredStudentId,
                "violin",
                HoveredStudentId.HasValue ? (position.X, position.Y) : null));
        }
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
}
