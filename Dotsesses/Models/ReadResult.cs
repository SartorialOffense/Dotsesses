namespace Dotsesses.Models;

/// <summary>
/// Outcome of reading a score spreadsheet: the parsed students plus any
/// non-fatal warnings the reader detected (duplicate columns, sparse
/// columns, skipped rows, etc.). Hard errors (no ID column, no readable
/// rows) still propagate as exceptions from the reader.
/// </summary>
public record ReadResult(
    IReadOnlyList<StudentAssessment> Students,
    IReadOnlyList<ReadWarning> Warnings);
