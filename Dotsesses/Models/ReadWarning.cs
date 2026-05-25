namespace Dotsesses.Models;

/// <summary>
/// Categories of issues the score-file reader can detect on load.
/// The view groups warnings of the same kind into one dialog entry.
/// </summary>
public enum ReadWarningKind
{
    /// <summary>No "Total" column; one was synthesized as the sum of all numeric columns.</summary>
    NoTotalColumn,
    /// <summary>The header row contained the same column name more than once.</summary>
    DuplicateColumnHeader,
    /// <summary>A "(Notes)" column was present whose base score name didn't match any score column.</summary>
    OrphanNotesColumn,
    /// <summary>Two or more student rows shared the same ID.</summary>
    DuplicateStudentId,
    /// <summary>One or more rows were skipped because they had no parseable student ID.</summary>
    SkippedRows,
    /// <summary>A score column had values for fewer students than the total enrolment.</summary>
    SparseColumn,
    /// <summary>A score column had the same value for every student (degenerate distribution).</summary>
    ConstantColumn,
    /// <summary>A column contained non-numeric values and was auto-classified as a categorical Student Attribute.</summary>
    CategoricalColumnDetected,
}

/// <summary>
/// Single load-time warning surfaced to the user.
/// </summary>
public record ReadWarning(ReadWarningKind Kind, string Message);
