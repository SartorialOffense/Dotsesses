namespace Dotsesses.Models;

/// <summary>
/// Per-series metadata carried alongside the correlation matrix payload so the
/// Python renderer no longer has to infer column roles from position (ADR-0018
/// slice 1). Keyed by formatted series name in the correlation pipeline.
/// </summary>
/// <param name="Type">
/// The column's <see cref="ScoreColumnType"/> — drives the Pearson/Spearman
/// split (slice 3): an Ordinal-touching cell uses Spearman.
/// </param>
/// <param name="BiasCorrect">
/// True iff this column should be rest-score de-biased against Total — i.e. the
/// user set its <see cref="ScoreSelection.BiasCorrect"/> flag (Numeric, non-Total).
/// The sole trigger for the correction, decoupled from aggregate membership so a
/// composite column (e.g. <c>Q1-Q4</c>) can be de-biased without being aggregated
/// (ADR-0018).
/// </param>
/// <param name="IsTotal">
/// True for the Total series (a Score named "Total", case-insensitive, with no
/// Index). Replaces the old "Total is the last series" positional assumption;
/// the red Total styling keys off this flag.
/// </param>
public record CorrelationColumnInfo(
    ScoreColumnType Type,
    bool BiasCorrect,
    bool IsTotal);
