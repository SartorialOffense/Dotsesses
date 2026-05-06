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

        // Find neighbors by grade order
        // Better grade (lower order number, higher score) - e.g., A when enabling B
        var betterGrade = sorted.LastOrDefault(c => c.Grade.Order < gradeToEnable.Order);
        // Worse grade (higher order number, lower score) - e.g., C when enabling B
        var worseGrade = sorted.FirstOrDefault(c => c.Grade.Order > gradeToEnable.Order);

        int proposedScore;

        if (betterGrade != null && worseGrade != null)
        {
            // Between two cursors - place at midpoint
            proposedScore = (betterGrade.Score + worseGrade.Score) / 2;
        }
        else if (worseGrade != null)
        {
            // At top edge (best grade) - place above the worse grade's cursor
            proposedScore = Math.Min(maxScore, worseGrade.Score + 10);
        }
        else if (betterGrade != null)
        {
            // At bottom edge (worst grade) - place below the better grade's cursor
            proposedScore = Math.Max(minScore, betterGrade.Score - 10);
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
    /// Public defensive fallback used by <c>SeedCursorsFromDefaults</c> when the default-curve
    /// placement produces non-monotonic cutoffs on a narrow aggregate range
    /// (M002/S05/T03 — mirrors MEM028's empty-aggregate guard for the single-narrow-component case).
    /// </summary>
    /// <remarks>
    /// Best grade (Grade.Order = 0) is placed at <paramref name="maxScore"/>; worst grade is placed
    /// at <paramref name="minScore"/>; intermediate grades are spread linearly between them.
    /// Result is monotonic non-strict; ties are tolerated by GradeAssigner (which uses &lt;, not ≤,
    /// for its ordering check). Sufficient for guaranteeing the GradeAssigner constructor does not
    /// throw on narrow ranges.
    /// </remarks>
    public IReadOnlyCollection<GradeCutoff> ResetToEvenSpacingMonotonic(
        IReadOnlyCollection<Grade> grades,
        int minScore,
        int maxScore)
    {
        ArgumentNullException.ThrowIfNull(grades);

        var sorted = grades.OrderBy(g => g.Order).ToList();
        var cutoffs = new List<GradeCutoff>();
        if (sorted.Count == 0) return cutoffs;

        int range = Math.Max(0, maxScore - minScore);
        var divisor = Math.Max(1, sorted.Count - 1);

        for (int i = 0; i < sorted.Count; i++)
        {
            // Best grade (Order=0, i=0) → maxScore; worst (i = count-1) → minScore.
            // For count==1 we centre at maxScore (degenerate but defined).
            var fraction = sorted.Count == 1 ? 1.0 : 1.0 - ((double)i / divisor);
            int score = (int)Math.Round(minScore + (range * fraction));
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
