namespace Dotsesses.Models;

/// <summary>
/// Represents a user selection for a score column controlling display, aggregate inclusion, and correlation inclusion.
/// </summary>
/// <param name="Name">Score name (matches <see cref="Score.Name"/>)</param>
/// <param name="Index">Optional index for multiple scores of same name (matches <see cref="Score.Index"/>)</param>
/// <param name="Display">Whether this score is shown in the UI</param>
/// <param name="Aggregate">Whether this score contributes to the computed aggregate</param>
/// <param name="Correlation">Whether this score participates in the correlation matrix</param>
public record ScoreSelection(string Name, int? Index, bool Display, bool Aggregate, bool Correlation);
