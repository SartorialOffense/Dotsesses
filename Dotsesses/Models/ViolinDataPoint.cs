namespace Dotsesses.Models;

/// <summary>
/// Represents a single swarm point in the violin plot.
/// <para>
/// <see cref="ValueLabel"/> is the display label shown on hover instead of
/// the numeric <see cref="Value"/> when the series is an Ordinal column
/// (e.g. "✔✔+" rather than 3); empty for ordinary numeric series (ADR-0017).
/// </para>
/// </summary>
public record ViolinDataPoint(
    double X,
    double Y,
    int StudentId,
    string Series,
    string Color,
    double Value,
    double SigmaValue,
    string Comment = "",
    string MuppetName = "",
    string ValueLabel = "");
