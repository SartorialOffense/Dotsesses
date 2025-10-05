namespace Dotsesses.Models;

/// <summary>
/// Represents a grade threshold - the minimum score required for a grade.
/// </summary>
/// <param name="Grade">The grade this cutoff represents</param>
/// <param name="Score">Minimum score threshold for this grade</param>
public record GradeCutoff(Grade Grade, int Score);
