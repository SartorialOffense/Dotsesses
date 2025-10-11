namespace Dotsesses.Models;

/// <summary>
/// Represents a single swarm point in the violin plot.
/// </summary>
public record ViolinDataPoint(
    double X,
    double Y,
    int StudentId,
    string Series,
    string Color,
    double Value);
