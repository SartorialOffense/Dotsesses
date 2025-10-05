namespace Dotsesses.Models;

/// <summary>
/// Represents a letter grade with its order in the grading scale.
/// </summary>
/// <param name="LetterGrade">The letter grade value</param>
/// <param name="Order">Position in grade hierarchy (A=0, A-=1, etc.)</param>
public record Grade(LetterGrade LetterGrade, int Order);
