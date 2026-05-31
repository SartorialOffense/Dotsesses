namespace Dotsesses.Models;

/// <summary>
/// Represents a single point in the correlation matrix plot.
/// Each point appears in a specific cell of the NxN grid.
///
/// The cell-level inference fields (<see cref="R"/>, <see cref="PValue"/>,
/// <see cref="N"/>, <see cref="Method"/>, <see cref="Corrected"/>) are repeated
/// on every point in a cell — dots are the only thing the overlay hit-tests, so
/// the hover tooltip reads them off whichever dot the cursor finds (ADR-0018
/// slice 3, mirroring the Significance Matrix's per-dot p-value).
/// </summary>
/// <param name="R">Signed correlation coefficient for the cell — Pearson r or
/// Spearman ρ depending on <paramref name="Method"/>. r² is <c>R*R</c>.</param>
/// <param name="PValue">Raw (uncorrected) two-sided p-value; null when the cell
/// is untestable.</param>
/// <param name="N">Number of common students the coefficient was computed over.</param>
/// <param name="Method">"pearson" or "spearman" (Spearman for any Ordinal-touching cell).</param>
/// <param name="Corrected">True when the cell used the rest-score de-bias (slice 2).</param>
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
    double R = 0.0,
    double? PValue = null,
    int N = 0,
    string Method = "pearson",
    bool Corrected = false,
    string MuppetName = "");
