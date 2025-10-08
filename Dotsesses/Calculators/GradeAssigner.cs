namespace Dotsesses.Calculators;

using Dotsesses.Models;

/// <summary>
/// Single source of truth for assigning grades to students based on cutoffs.
/// </summary>
public class GradeAssigner
{
    private readonly List<GradeCutoff> _sortedCutoffs;
    private readonly Grade _lowestGrade;

    /// <summary>
    /// Creates a new GradeAssigner with validated and pre-sorted cutoffs.
    /// Create a new instance whenever grade cutoffs change.
    /// </summary>
    /// <param name="cutoffs">Grade cutoffs to use for assignment</param>
    /// <exception cref="InvalidOperationException">Thrown if cutoffs are invalid or out of order</exception>
    public GradeAssigner(IReadOnlyCollection<GradeCutoff> cutoffs)
    {
        ArgumentNullException.ThrowIfNull(cutoffs);

        if (!cutoffs.Any())
        {
            throw new InvalidOperationException("No grades available in cutoffs");
        }

        // Sort by grade hierarchy (Order) once for both validation and finding lowest grade
        var sortedByGrade = cutoffs.OrderBy(c => c.Grade.Order).ToList();
        
        // The lowest grade is the last element (highest Order)
        _lowestGrade = sortedByGrade[^1].Grade;

        // VALIDATE: Cutoffs must be properly ordered (better grades >= worse grades)
        ValidateCutoffOrdering(sortedByGrade, _lowestGrade);
        
        // Pre-sort cutoffs by descending score order, excluding lowest grade
        // The lowest grade is the fallback if score is below all other cutoffs
        _sortedCutoffs = cutoffs
            .Where(c => !c.Grade.Equals(_lowestGrade))
            .OrderByDescending(c => c.Score)
            .ToList();
    }

    /// <summary>
    /// Assigns a grade to a student based on the cutoffs provided at construction.
    /// </summary>
    /// <param name="score">Student's aggregate score</param>
    /// <returns>The grade the student qualifies for</returns>
    public Grade AssignGrade(int score)
    {
        foreach (var cutoff in _sortedCutoffs)
        {
            if (score >= cutoff.Score)
            {
                return cutoff.Grade;
            }
        }

        // If below all cutoffs, return lowest grade (catch-all)
        return _lowestGrade;
    }

    private void ValidateCutoffOrdering(List<GradeCutoff> sortedByGrade, Grade lowestGrade)
    {
        // Verify: Better grades (lower Order) must have >= cutoff scores than worse grades
        // EXCEPT the lowest grade, which is a catch-all and has a static score
        for (int i = 0; i < sortedByGrade.Count - 1; i++)
        {
            var betterGrade = sortedByGrade[i];
            var worseGrade = sortedByGrade[i + 1];

            // Skip validation if worse grade is the catch-all lowest grade
            if (worseGrade.Grade.Equals(lowestGrade))
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