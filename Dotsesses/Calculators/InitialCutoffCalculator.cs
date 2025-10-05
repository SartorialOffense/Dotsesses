namespace Dotsesses.Calculators;

using Dotsesses.Models;

/// <summary>
/// Calculates initial cursor placements to match the default curve.
/// Handles tie situations by allowing overflow rather than splitting tied students.
/// </summary>
public class InitialCutoffCalculator
{
    /// <summary>
    /// Places cutoffs to match target counts from default curve.
    /// </summary>
    /// <param name="assessments">All student assessments</param>
    /// <param name="defaultCurve">Target grade distribution</param>
    /// <returns>Grade cutoffs positioned to match curve</returns>
    public IReadOnlyCollection<GradeCutoff> Calculate(
        IReadOnlyCollection<StudentAssessment> assessments,
        IReadOnlyCollection<CutoffCount> defaultCurve)
    {
        ArgumentNullException.ThrowIfNull(assessments);
        ArgumentNullException.ThrowIfNull(defaultCurve);

        var sortedStudents = assessments
            .OrderByDescending(a => a.AggregateGrade)
            .ThenBy(a => a.Id)
            .ToList();

        var sortedCurve = defaultCurve.OrderBy(cc => cc.Grade.Order).ToList();
        var cutoffs = new List<GradeCutoff>();

        int currentIndex = 0;

        // Process each grade from highest to lowest
        foreach (var curveEntry in sortedCurve)
        {
            if (currentIndex >= sortedStudents.Count)
            {
                // No more students - place cutoff at 0
                cutoffs.Add(new GradeCutoff(curveEntry.Grade, 0));
                continue;
            }

            int targetCount = curveEntry.Count;
            int endIndex = Math.Min(currentIndex + targetCount, sortedStudents.Count);

            // Handle ties at boundary - include all tied students
            if (endIndex < sortedStudents.Count)
            {
                int boundaryScore = sortedStudents[endIndex - 1].AggregateGrade;
                while (endIndex < sortedStudents.Count &&
                       sortedStudents[endIndex].AggregateGrade == boundaryScore)
                {
                    endIndex++;
                }
            }

            // Cutoff is the minimum score for students who received this grade (accounting for ties)
            int cutoffScore = sortedStudents[endIndex - 1].AggregateGrade;
            cutoffs.Add(new GradeCutoff(curveEntry.Grade, cutoffScore));

            currentIndex = endIndex;
        }

        return cutoffs;
    }
}
