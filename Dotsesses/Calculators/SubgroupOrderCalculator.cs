namespace Dotsesses.Calculators;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Computes the canonical left-to-right order of a categorical column's
/// Subgroup labels for the Significance Matrix (ADR-0017). Ownership of this
/// ordering lives in C#; the Python renderer consumes the resulting list
/// verbatim (it no longer sorts).
///
/// Rule:
/// <list type="bullet">
/// <item>Labels carrying a <c>~N</c> SortOrder come first, ascending by N.</item>
/// <item>Unsuffixed labels follow, alphabetical (ordinal) among themselves.</item>
/// <item>Ties on N break alphabetically.</item>
/// </list>
/// Same-label / different-N conflicts are already resolved to the minimum N at
/// read time (see <see cref="Dotsesses.Services.ScoreReader"/>); if a label is
/// nonetheless observed with and without a SortOrder, the minimum present N
/// wins and it is treated as suffixed.
/// </summary>
public static class SubgroupOrderCalculator
{
    /// <summary>
    /// Returns the distinct labels in canonical order. Input is the per-student
    /// (label, sortOrder) observations for one column; duplicates are collapsed.
    /// </summary>
    public static List<string> Order(IEnumerable<(string Label, int? SortOrder)> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var byLabel = values
            .GroupBy(v => v.Label, StringComparer.Ordinal)
            .Select(g =>
            {
                var orders = g.Where(x => x.SortOrder.HasValue)
                              .Select(x => x.SortOrder!.Value)
                              .ToList();
                int? order = orders.Count > 0 ? orders.Min() : (int?)null;
                return (Label: g.Key, Order: order);
            })
            .ToList();

        var suffixed = byLabel
            .Where(x => x.Order.HasValue)
            .OrderBy(x => x.Order!.Value)
            .ThenBy(x => x.Label, StringComparer.Ordinal);

        var unsuffixed = byLabel
            .Where(x => !x.Order.HasValue)
            .OrderBy(x => x.Label, StringComparer.Ordinal);

        return suffixed.Concat(unsuffixed).Select(x => x.Label).ToList();
    }
}
