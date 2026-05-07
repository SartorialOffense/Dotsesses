namespace Dotsesses.Models;

/// <summary>
/// Outcome of GradingSession.CanMoveCutoff / MoveCutoff. Success
/// carries no failure; rejected moves leave session state untouched
/// and report which constraint was violated.
/// </summary>
public sealed record CutoffMoveResult(bool Success, CutoffMoveFailure? Failure)
{
    public static CutoffMoveResult Ok() => new(true, null);
    public static CutoffMoveResult Fail(CutoffMoveFailure failure) => new(false, failure);
}

public enum CutoffMoveFailure
{
    OutOfRange,
    WouldOverlap,
    OrderingViolation,
    GradeNotEnabled,
}
