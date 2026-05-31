namespace Dotsesses.Calculators;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Computes the data-driven default column selection for each plot when a
/// fresh dataset loads, so a 30–40-column gradebook doesn't open with every
/// column on in every plot (see ADR-0016). Pure and deterministic — keyed by
/// the same *series name* the plots join on, so the caller can apply results
/// by recomputing each <c>ScoreSelection</c>'s series name.
///
/// The three rules:
/// <list type="bullet">
///   <item><b>Distribution</b>: Total + the first N non-Total numerics in
///   column order.</item>
///   <item><b>Correlation</b>: the columns appearing in the top-K highest-r²
///   pairs (Total excluded from the ranking), plus Total.</item>
///   <item><b>Significance</b>: categoricals whose smallest p across numerics
///   is ≤ a threshold, each with its top-M smallest-p numerics, unioned.</item>
/// </list>
/// </summary>
public static class PlotSelectionCalculator
{
    /// <summary>
    /// Distribution (violin) selection: <paramref name="totalName"/> always,
    /// then the first <paramref name="extra"/> non-Total numeric series in the
    /// given (left-to-right column) order.
    /// </summary>
    public static HashSet<string> SelectDistribution(
        IReadOnlyList<string> orderedNumericNames,
        string? totalName,
        int extra = 10)
    {
        ArgumentNullException.ThrowIfNull(orderedNumericNames);

        var result = new HashSet<string>(StringComparer.Ordinal);
        if (totalName != null) result.Add(totalName);

        int added = 0;
        foreach (var name in orderedNumericNames)
        {
            if (added >= extra) break;
            if (totalName != null && string.Equals(name, totalName, StringComparison.Ordinal)) continue;
            if (result.Add(name)) added++;
        }
        return result;
    }

    /// <summary>
    /// Correlation selection: rank every pair of non-Total numeric series by
    /// Pearson r², take the top <paramref name="topPairs"/>, select every
    /// series appearing in them, and always include <paramref name="totalName"/>.
    /// (Total is excluded from the ranking because it tracks its components and
    /// would otherwise win every slot.)
    /// </summary>
    public static HashSet<string> SelectCorrelation(
        IReadOnlyList<(string Name, IReadOnlyDictionary<string, double> Values)> numericSeries,
        string? totalName,
        int topPairs = 4)
    {
        ArgumentNullException.ThrowIfNull(numericSeries);

        var result = new HashSet<string>(StringComparer.Ordinal);
        if (totalName != null) result.Add(totalName);

        var cols = numericSeries
            .Where(s => totalName == null || !string.Equals(s.Name, totalName, StringComparison.Ordinal))
            .ToList();

        var pairs = new List<(string A, string B, double R2)>();
        for (int i = 0; i < cols.Count; i++)
        {
            for (int j = i + 1; j < cols.Count; j++)
            {
                var r2 = PearsonR2(cols[i].Values, cols[j].Values);
                if (r2.HasValue) pairs.Add((cols[i].Name, cols[j].Name, r2.Value));
            }
        }

        foreach (var p in pairs
                     .OrderByDescending(p => p.R2)
                     .ThenBy(p => p.A, StringComparer.Ordinal)
                     .ThenBy(p => p.B, StringComparer.Ordinal)
                     .Take(topPairs))
        {
            result.Add(p.A);
            result.Add(p.B);
        }
        return result;
    }

    /// <summary>
    /// Significance selection. <paramref name="pLookup"/> returns the omnibus
    /// p-value for a (numeric, categorical) cell, or null when untestable. A
    /// categorical *qualifies* if its smallest p across numerics is ≤
    /// <paramref name="threshold"/>; for each qualifier the
    /// <paramref name="topNumerics"/> numerics with the smallest p are added.
    /// Returns the union of qualifying categoricals and their selected numerics.
    /// Categoricals named in <paramref name="excludedCategoricalNames"/>
    /// (case-insensitive) are never auto-selected — e.g. a Grade column, which
    /// is derived from the scores and would test as trivially significant.
    /// </summary>
    public static HashSet<string> SelectSignificance(
        IReadOnlyList<string> numericNames,
        IReadOnlyList<string> categoricalNames,
        Func<string, string, double?> pLookup,
        double threshold = 0.2,
        int topNumerics = 3,
        IReadOnlyCollection<string>? excludedCategoricalNames = null)
    {
        ArgumentNullException.ThrowIfNull(numericNames);
        ArgumentNullException.ThrowIfNull(categoricalNames);
        ArgumentNullException.ThrowIfNull(pLookup);

        var excluded = new HashSet<string>(
            excludedCategoricalNames ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);

        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cat in categoricalNames)
        {
            if (excluded.Contains(cat)) continue;

            var ps = numericNames
                .Select(n => (Name: n, P: pLookup(n, cat)))
                .Where(t => t.P.HasValue)
                .Select(t => (t.Name, P: t.P!.Value))
                .ToList();
            if (ps.Count == 0) continue;
            if (ps.Min(t => t.P) > threshold) continue;

            result.Add(cat);
            foreach (var t in ps
                         .OrderBy(t => t.P)
                         .ThenBy(t => t.Name, StringComparer.Ordinal)
                         .Take(topNumerics))
            {
                result.Add(t.Name);
            }
        }
        return result;
    }

    /// <summary>
    /// Pearson r² between two series over their common student ids. Returns
    /// null when fewer than two ids overlap or either side has zero variance
    /// (r undefined).
    /// </summary>
    public static double? PearsonR2(
        IReadOnlyDictionary<string, double> a,
        IReadOnlyDictionary<string, double> b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        double sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0;
        int n = 0;
        foreach (var kv in a)
        {
            if (!b.TryGetValue(kv.Key, out var y)) continue;
            double x = kv.Value;
            sx += x; sy += y; sxx += x * x; syy += y * y; sxy += x * y; n++;
        }
        if (n < 2) return null;

        double covN = sxy - sx * sy / n;
        double varX = sxx - sx * sx / n;
        double varY = syy - sy * sy / n;
        if (varX <= 0 || varY <= 0) return null;

        double r = covN / Math.Sqrt(varX * varY);
        double r2 = r * r;
        return double.IsNaN(r2) ? null : r2;
    }
}
