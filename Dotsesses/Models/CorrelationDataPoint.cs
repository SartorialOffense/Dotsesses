namespace Dotsesses.Models;

/// <summary>
/// Represents a single point in the correlation matrix plot.
/// Each point appears in a specific cell of the NxN grid.
/// </summary>
public record CorrelationDataPoint(
    int CellRow,
    int CellCol,
    double X,
    double Y,
    int StudentId,
    string XSeries,
    string YSeries,
    double XValue,
    double YValue,
    string Color,
    string MuppetName = "");
