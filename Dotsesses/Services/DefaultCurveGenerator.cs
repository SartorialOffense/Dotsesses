namespace Dotsesses.Services;

using Dotsesses.Models;

/// <summary>
/// Generates the default school curve distribution.
/// </summary>
public class DefaultCurveGenerator
{
    /// <summary>
    /// Grade percentage ranges for the standard curve.
    /// Grades not in this dictionary have no predefined range.
    /// </summary>
    private static readonly Dictionary<LetterGrade, (double MinPercent, double MaxPercent)> GradePercentages = new()
    {
        { LetterGrade.A, (0.05, 0.10) },      // 5% - 10%
        { LetterGrade.AMinus, (0.05, 0.15) }, // 5% - 15%
        { LetterGrade.BPlus, (0.10, 0.20) },  // 10% - 20%
        { LetterGrade.B, (0.20, 0.30) },      // 20% - 30%
        { LetterGrade.BMinus, (0.10, 0.25) }, // 10% - 25%
        { LetterGrade.CPlus, (0.10, 0.25) },  // 10% - 25%
        { LetterGrade.C, (0.05, 0.20) }       // 5% - 20%
    };

    /// <summary>
    /// Creates the standard curve with midpoint counts based on student count.
    /// </summary>
    public IReadOnlyCollection<CutoffCount> Generate(int studentCount)
    {
        var result = new List<CutoffCount>();

        foreach (var (letterGrade, (minPercent, maxPercent)) in GradePercentages)
        {
            var grade = new Grade(letterGrade, (int)letterGrade);
            var midpoint = (int)Math.Round((minPercent + maxPercent) / 2 * studentCount);
            result.Add(new CutoffCount(grade, midpoint));
        }

        return result;
    }

    /// <summary>
    /// Creates the standard curve with percentage-based ranges.
    /// </summary>
    /// <param name="studentCount">Total number of students in the class</param>
    public IReadOnlyCollection<CutoffCountRange> GenerateRanges(int studentCount)
    {
        var result = new List<CutoffCountRange>();

        foreach (var grade in GetAllGrades())
        {
            if (GradePercentages.TryGetValue(grade.LetterGrade, out var percentages))
            {
                var lower = (int)Math.Round(percentages.MinPercent * studentCount);
                var upper = (int)Math.Round(percentages.MaxPercent * studentCount);
                result.Add(new CutoffCountRange(grade, lower, upper));
            }
            else
            {
                // Grades without predefined ranges (C-, D+, D, F)
                result.Add(new CutoffCountRange(grade, 0, 0));
            }
        }

        return result;
    }

    /// <summary>
    /// Gets all possible grades in the system.
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
            new Grade(LetterGrade.CMinus, 7),
            new Grade(LetterGrade.DPlus, 8),
            new Grade(LetterGrade.D, 9),
            new Grade(LetterGrade.F, 10)
        };
    }
}
