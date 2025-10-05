namespace Dotsesses.Calculators;

using Dotsesses.Models;

/// <summary>
/// Handles cursor placement when enabling new grades.
/// </summary>
public class CursorPlacementCalculator
{
    private const int MinimumCursorSpacing = 1;

    /// <summary>
    /// Calculates new cursor position when enabling a grade.
    /// </summary>
    /// <param name="gradeToEnable">Grade being enabled</param>
    /// <param name="existingCutoffs">Currently enabled cutoffs</param>
    /// <param name="minScore">Minimum score in dataset</param>
    /// <param name="maxScore">Maximum score in dataset</param>
    /// <returns>New cutoff collection including the newly enabled grade</returns>
    public IReadOnlyCollection<GradeCutoff> PlaceNewCursor(
        Grade gradeToEnable,
        IReadOnlyCollection<GradeCutoff> existingCutoffs,
        int minScore,
        int maxScore)
    {
        ArgumentNullException.ThrowIfNull(gradeToEnable);
        ArgumentNullException.ThrowIfNull(existingCutoffs);

        var sorted = existingCutoffs.OrderBy(c => c.Grade.Order).ToList();

        // Find neighbors
        var lowerGrade = sorted.LastOrDefault(c => c.Grade.Order < gradeToEnable.Order);
        var higherGrade = sorted.FirstOrDefault(c => c.Grade.Order > gradeToEnable.Order);

        int proposedScore;

        if (lowerGrade != null && higherGrade != null)
        {
            // Between two cursors - place at midpoint
            proposedScore = (lowerGrade.Score + higherGrade.Score) / 2;
        }
        else if (higherGrade != null)
        {
            // At bottom edge - place below highest cursor
            proposedScore = Math.Max(minScore, higherGrade.Score - 10);
        }
        else if (lowerGrade != null)
        {
            // At top edge - place above lowest cursor
            proposedScore = Math.Min(maxScore, lowerGrade.Score + 10);
        }
        else
        {
            // First cursor - place at midpoint of range
            proposedScore = (minScore + maxScore) / 2;
        }

        var newCutoffs = sorted.ToList();
        newCutoffs.Add(new GradeCutoff(gradeToEnable, proposedScore));

        // Check for overlaps
        if (HasOverlaps(newCutoffs))
        {
            // Reset all to even spacing
            return ResetToEvenSpacing(newCutoffs.Select(c => c.Grade).ToList(), minScore, maxScore);
        }

        return newCutoffs;
    }

    /// <summary>
    /// Resets all enabled cursors to even spacing across score range.
    /// </summary>
    private IReadOnlyCollection<GradeCutoff> ResetToEvenSpacing(
        List<Grade> grades,
        int minScore,
        int maxScore)
    {
        var sorted = grades.OrderBy(g => g.Order).ToList();
        var cutoffs = new List<GradeCutoff>();

        int range = maxScore - minScore;
        int spacing = range / (sorted.Count + 1);

        for (int i = 0; i < sorted.Count; i++)
        {
            int score = minScore + (spacing * (i + 1));
            cutoffs.Add(new GradeCutoff(sorted[i], score));
        }

        return cutoffs;
    }

    /// <summary>
    /// Checks if any cursors violate minimum spacing requirement.
    /// </summary>
    private bool HasOverlaps(List<GradeCutoff> cutoffs)
    {
        var sorted = cutoffs.OrderByDescending(c => c.Score).ToList();

        for (int i = 0; i < sorted.Count - 1; i++)
        {
            if (sorted[i].Score - sorted[i + 1].Score < MinimumCursorSpacing)
            {
                return true;
            }
        }

        return false;
    }
}
