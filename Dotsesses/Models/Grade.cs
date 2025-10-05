namespace Dotsesses.Models;

/// <summary>
/// Represents a letter grade with its order in the grading scale.
/// </summary>
/// <param name="LetterGrade">The letter grade value</param>
/// <param name="Order">Position in grade hierarchy (A=0, A-=1, etc.)</param>
public record Grade(LetterGrade LetterGrade, int Order)
{
    /// <summary>
    /// Gets the display name for the grade (e.g., "D-" instead of "DMinus").
    /// </summary>
    public string DisplayName => LetterGrade switch
    {
        Models.LetterGrade.A => "A",
        Models.LetterGrade.AMinus => "A-",
        Models.LetterGrade.BPlus => "B+",
        Models.LetterGrade.B => "B",
        Models.LetterGrade.BMinus => "B-",
        Models.LetterGrade.CPlus => "C+",
        Models.LetterGrade.C => "C",
        Models.LetterGrade.D => "D",
        Models.LetterGrade.DMinus => "D-",
        Models.LetterGrade.F => "F",
        _ => LetterGrade.ToString()
    };
}
