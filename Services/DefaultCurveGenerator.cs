namespace Dotsesses.Services;

using Dotsesses.Models;

/// <summary>
/// Generates the default school curve distribution.
/// </summary>
public class DefaultCurveGenerator
{
    /// <summary>
    /// Creates the standard curve: A, A-, B+, B, B-, C+, C.
    /// Grades below C are not required in the curve.
    /// </summary>
    public IReadOnlyCollection<CutoffCount> Generate()
    {
        return new List<CutoffCount>
        {
            new CutoffCount(new Grade(LetterGrade.A, 0), 5),
            new CutoffCount(new Grade(LetterGrade.AMinus, 1), 8),
            new CutoffCount(new Grade(LetterGrade.BPlus, 2), 15),
            new CutoffCount(new Grade(LetterGrade.B, 3), 20),
            new CutoffCount(new Grade(LetterGrade.BMinus, 4), 18),
            new CutoffCount(new Grade(LetterGrade.CPlus, 5), 12),
            new CutoffCount(new Grade(LetterGrade.C, 6), 10)
        };
    }

    /// <summary>
    /// Gets all possible grades in the system (for UI checkboxes).
    /// </summary>
    public IReadOnlyCollection<Grade> GetAllGrades()
    {
        return new List<Grade>
        {
            new Grade(LetterGrade.A, 0),
            new Grade(LetterGrade.AMinus, 1),
            new Grade(LetterGrade.BPlus, 2),
            new Grade(LetterGrade.B, 3),
            new Grade(LetterGrade.BMinus, 4),
            new Grade(LetterGrade.CPlus, 5),
            new Grade(LetterGrade.C, 6),
            new Grade(LetterGrade.D, 7),
            new Grade(LetterGrade.DMinus, 8),
            new Grade(LetterGrade.F, 9)
        };
    }
}
