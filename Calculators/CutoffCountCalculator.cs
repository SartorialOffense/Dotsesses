namespace Dotsesses.Calculators;

using Dotsesses.Models;

/// <summary>
/// Calculates the count of students in each grade given cutoff thresholds.
/// </summary>
public class CutoffCountCalculator
{
    /// <summary>
    /// Calculates student counts for each grade based on current cutoffs.
    /// </summary>
    /// <param name="assessments">All student assessments</param>
    /// <param name="cutoffs">Current grade cutoffs (sorted by score descending)</param>
    /// <returns>Count of students in each grade</returns>
    public IReadOnlyCollection<CutoffCount> Calculate(
        IReadOnlyCollection<StudentAssessment> assessments,
        IReadOnlyCollection<GradeCutoff> cutoffs)
    {
        ArgumentNullException.ThrowIfNull(assessments);
        ArgumentNullException.ThrowIfNull(cutoffs);

        var sortedCutoffs = cutoffs.OrderByDescending(c => c.Score).ToList();
        var counts = new Dictionary<Grade, int>();

        // Initialize counts
        foreach (var cutoff in sortedCutoffs)
        {
            counts[cutoff.Grade] = 0;
        }

        // Bin each student into appropriate grade
        foreach (var assessment in assessments)
        {
            var grade = GetGradeForScore(assessment.AggregateGrade, sortedCutoffs);
            if (grade != null)
            {
                counts[grade]++;
            }
        }

        return counts
            .Select(kvp => new CutoffCount(kvp.Key, kvp.Value))
            .OrderBy(cc => cc.Grade.Order)
            .ToList();
    }

    private Grade? GetGradeForScore(int score, List<GradeCutoff> sortedCutoffs)
    {
        // Find highest grade where score meets cutoff
        foreach (var cutoff in sortedCutoffs)
        {
            if (score >= cutoff.Score)
            {
                return cutoff.Grade;
            }
        }

        // If below all cutoffs, gets the lowest grade
        return sortedCutoffs.LastOrDefault()?.Grade;
    }
}
