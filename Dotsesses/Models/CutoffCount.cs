namespace Dotsesses.Models;

/// <summary>
/// Represents the count of students receiving a particular grade.
/// </summary>
/// <param name="Grade">The grade</param>
/// <param name="Count">Number of students with this grade</param>
public record CutoffCount(Grade Grade, int Count);

/// <summary>
/// Represents a target count range for students receiving a particular grade.
/// </summary>
/// <param name="Grade">The grade</param>
/// <param name="LowerBound">Minimum target count (inclusive)</param>
/// <param name="UpperBound">Maximum target count (inclusive)</param>
public record CutoffCountRange(Grade Grade, int LowerBound, int UpperBound)
{
    /// <summary>
    /// Gets the midpoint of the range (for cursor placement calculations).
    /// </summary>
    public int Midpoint => (LowerBound + UpperBound) / 2;

    /// <summary>
    /// Display format as "L-U" (e.g., "3-7").
    /// </summary>
    public string DisplayRange => $"{LowerBound}-{UpperBound}";
}
