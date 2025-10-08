namespace Dotsesses.Calculators;

using Dotsesses.Models;

/// <summary>
/// Validates cursor movements and enforces constraints.
/// </summary>
public class CursorValidation
{
    private const int MinimumCursorSpacing = 1;

    /// <summary>
    /// Validates a proposed cursor movement.
    /// </summary>
    /// <param name="gradeToMove">Grade being moved</param>
    /// <param name="proposedScore">New score position</param>
    /// <param name="allCutoffs">All current cutoffs including the one being moved</param>
    /// <returns>Validated score (may be adjusted to prevent overlap)</returns>
    public int ValidateMovement(
        Grade gradeToMove,
        int proposedScore,
        IReadOnlyCollection<GradeCutoff> allCutoffs)
    {
        ArgumentNullException.ThrowIfNull(gradeToMove);
        ArgumentNullException.ThrowIfNull(allCutoffs);

        var others = allCutoffs.Where(c => c.Grade.Order != gradeToMove.Order).ToList();

        // Find the lowest grade (highest Order) - this is the catch-all grade
        var lowestGrade = allCutoffs.OrderByDescending(c => c.Grade.Order).FirstOrDefault()?.Grade;

        // Find adjacent cursors by score (not grade order)
        // Lower score cursor (grade with higher order number, like C vs B)
        // BUT exclude the lowest grade - it's a catch-all with no draggable cursor
        var lowerScoreCursor = others
            .Where(c => c.Grade.Order > gradeToMove.Order)
            .Where(c => !c.Grade.Equals(lowestGrade))  // Exclude catch-all grade
            .OrderBy(c => c.Grade.Order)
            .FirstOrDefault();

        // Higher score cursor (grade with lower order number, like A vs B)
        var higherScoreCursor = others
            .Where(c => c.Grade.Order < gradeToMove.Order)
            .OrderByDescending(c => c.Grade.Order)
            .FirstOrDefault();

        // Enforce minimum spacing
        if (lowerScoreCursor != null)
        {
            int minAllowed = lowerScoreCursor.Score + MinimumCursorSpacing;
            proposedScore = Math.Max(proposedScore, minAllowed);
        }

        if (higherScoreCursor != null)
        {
            int maxAllowed = higherScoreCursor.Score - MinimumCursorSpacing;
            proposedScore = Math.Min(proposedScore, maxAllowed);
        }

        return proposedScore;
    }

    /// <summary>
    /// Checks if a set of cutoffs is valid (no overlaps, proper spacing).
    /// </summary>
    public bool IsValid(IReadOnlyCollection<GradeCutoff> cutoffs)
    {
        ArgumentNullException.ThrowIfNull(cutoffs);

        var sorted = cutoffs.OrderByDescending(c => c.Score).ToList();

        for (int i = 0; i < sorted.Count - 1; i++)
        {
            if (sorted[i].Score - sorted[i + 1].Score < MinimumCursorSpacing)
            {
                return false;
            }
        }

        return true;
    }
}
