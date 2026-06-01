namespace Dotsesses;

/// <summary>
/// Compile-time feature flags. Kept deliberately tiny — a home for temporary,
/// easily-reverted behavior switches that don't warrant configuration or
/// persistence.
/// </summary>
public static class FeatureFlags
{
    /// <summary>
    /// TEMPORARY (see <c>TECH_DEBT.md</c> TD010): take the score straight from
    /// the spreadsheet's <c>Total</c> column instead of computing it by summing
    /// the columns flagged <c>Aggregate</c>. While true, the Aggregate column in
    /// Settings is hidden and the per-column <c>Aggregate</c> flags are inert
    /// (they still persist, so flipping this back restores summed aggregation
    /// with no migration). The Bias-Correct de-bias is unaffected.
    ///
    /// Revert: set to <c>false</c> (or delete this flag and the branches that
    /// read it) once the column-relationship model lands.
    /// </summary>
    public const bool UseSpreadsheetTotal = true;
}
