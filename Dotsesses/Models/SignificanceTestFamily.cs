namespace Dotsesses.Models;

/// <summary>
/// The omnibus statistical test run per cell of the Significance Matrix
/// (see ADR-0014 slice 4). Both families are robust and reduce cleanly to
/// their two-group equivalents, so a single code path covers categorical
/// columns with 2 or 2+ values:
/// <list type="bullet">
///   <item><see cref="Parametric"/> — Welch's ANOVA (unequal-variance-safe;
///   reduces to Welch's t for 2 groups).</item>
///   <item><see cref="Nonparametric"/> — Kruskal–Wallis (rank-based;
///   reduces to Mann–Whitney for 2 groups).</item>
/// </list>
/// </summary>
public enum SignificanceTestFamily
{
    Parametric,
    Nonparametric,
}
