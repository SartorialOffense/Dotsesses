namespace Dotsesses.Models;

/// <summary>
/// Represents the count of students receiving a particular grade.
/// </summary>
/// <param name="Grade">The grade</param>
/// <param name="Count">Number of students with this grade</param>
public record CutoffCount(Grade Grade, int Count);
