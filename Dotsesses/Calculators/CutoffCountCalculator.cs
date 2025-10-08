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
    /// <param name="cutoffs">Current grade cutoffs</param>
    /// <returns>Count of students in each grade</returns>
    public IReadOnlyCollection<CutoffCount> Calculate(
        IReadOnlyCollection<StudentAssessment> assessments,
        IReadOnlyCollection<GradeCutoff> cutoffs)
    {
        ArgumentNullException.ThrowIfNull(assessments);
        ArgumentNullException.ThrowIfNull(cutoffs);

        var gradeAssigner = new GradeAssigner(cutoffs);
        var counts = new Dictionary<Grade, int>();

        // Initialize counts
        foreach (var cutoff in cutoffs)
        {
            counts[cutoff.Grade] = 0;
        }

        // Bin each student into appropriate grade using GradeAssigner
        foreach (var assessment in assessments)
        {
            var grade = gradeAssigner.AssignGrade(assessment.AggregateGrade);
            counts[grade]++;
        }

        return counts
            .Select(kvp => new CutoffCount(kvp.Key, kvp.Value))
            .OrderBy(cc => cc.Grade.Order)
            .ToList();
    }
}
