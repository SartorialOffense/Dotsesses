namespace Dotsesses.Models;

/// <summary>
/// Immutable snapshot of a Class's grading at one moment — current
/// GradeCutoffs, derived per-Grade CutoffCounts, and the set of
/// EnabledGrades. Every accepted change to a GradingSession produces
/// a new GradingState, carried as the State member of the
/// corresponding GradingStateChange. See ADR-0008.
/// </summary>
public sealed record GradingState(
    IReadOnlyList<GradeCutoff> Cutoffs,
    IReadOnlyList<CutoffCount> Counts,
    IReadOnlySet<Grade> EnabledGrades);
