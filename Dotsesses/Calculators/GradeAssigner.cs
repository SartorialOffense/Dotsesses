namespace Dotsesses.Calculators;

using Dotsesses.Models;

/// <summary>
/// Single source of truth for assigning grades to students based on cutoffs.
/// </summary>
public class GradeAssigner
{
    /// <summary>
    /// Assigns a grade to a student based on current cutoffs.
    /// Validates that cutoffs are properly ordered before assignment.
    /// </summary>
    /// <param name="score">Student's aggregate score</param>
    /// <param name="cutoffs">Current grade cutoffs</param>
    /// <returns>The grade the student qualifies for</returns>
    /// <exception cref="InvalidOperationException">Thrown if cutoffs are out of order (validation bug)</exception>
    public Grade AssignGrade(int score, IReadOnlyCollection<GradeCutoff> cutoffs)
    {
        ArgumentNullException.ThrowIfNull(cutoffs);

        if (!cutoffs.Any())
        {
            throw new InvalidOperationException("No grades available in cutoffs");
        }

        // VALIDATE: Cutoffs must be properly ordered (better grades >= worse grades)
        ValidateCutoffOrdering(cutoffs);

        // Find the lowest grade (catch-all grade with highest Order)
        var lowestGrade = cutoffs.OrderByDescending(c => c.Grade.Order).First();
        
        // Find grade by descending score order, excluding lowest grade
        // The lowest grade is the fallback if score is below all other cutoffs
        var sortedCutoffs = cutoffs
            .Where(c => !c.Grade.Equals(lowestGrade.Grade))
            .OrderByDescending(c => c.Score)
            .ToList();

        foreach (var cutoff in sortedCutoffs)
        {
            if (score >= cutoff.Score)
            {
                return cutoff.Grade;
            }
        }

        // If below all cutoffs, return lowest grade (catch-all)
        return lowestGrade.Grade;
    }

    private void ValidateCutoffOrdering(IReadOnlyCollection<GradeCutoff> cutoffs)
    {
        // Sort by grade hierarchy (Order)
        var sortedByGrade = cutoffs.OrderBy(c => c.Grade.Order).ToList();
        
        // Find the lowest grade (catch-all grade with highest Order)
        var lowestGrade = sortedByGrade.OrderByDescending(c => c.Grade.Order).First();

        // Verify: Better grades (lower Order) must have >= cutoff scores than worse grades
        // EXCEPT the lowest grade, which is a catch-all and has a static score
        for (int i = 0; i < sortedByGrade.Count - 1; i++)
        {
            var betterGrade = sortedByGrade[i];
            var worseGrade = sortedByGrade[i + 1];

            // Skip validation if worse grade is the catch-all lowest grade
            if (worseGrade.Grade.Equals(lowestGrade.Grade))
                continue;

            if (betterGrade.Score < worseGrade.Score)
            {
                throw new InvalidOperationException(
                    $"Cutoffs are out of order: {betterGrade.Grade.DisplayName} " +
                    $"(score {betterGrade.Score}) has a lower cutoff than " +
                    $"{worseGrade.Grade.DisplayName} (score {worseGrade.Score}). " +
                    $"This indicates a bug in cursor validation."
                );
            }
        }
    }
}