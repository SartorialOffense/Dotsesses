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

        // Extract SVG dimensions from viewBox (simplified - would need XML parsing for real implementation)
        // For now, assume standard matplotlib dimensions
        _svgWidth = widthInches * 72; // Points
        _svgHeight = heightInches * 72;
    }

    /// <summary>
    /// Handles pointer moved event for hover detection.
    /// </summary>
    public void OnPointerMoved(Point position)
    {
        if (_dataPoints.Count == 0 || _displayWidth == 0 || _displayHeight == 0)
            return;

        // Calculate scale factors for SVG to display conversion
        double scaleX = _displayWidth / _svgWidth;
        double scaleY = _displayHeight / _svgHeight;

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
    /// Converts SVG coordinates to display coordinates.
    /// </summary>
    public (double X, double Y) SvgToDisplay(double svgX, double svgY)
    {
        if (_svgWidth == 0 || _svgHeight == 0)
            return (0, 0);

        double scaleX = _displayWidth / _svgWidth;
        double scaleY = _displayHeight / _svgHeight;

        return (svgX * scaleX, svgY * scaleY);
    }
}
