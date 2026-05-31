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
/// <param name="IsAggregateComponent">
/// True iff this column is a Numeric column summed into the AggregateScore
/// (<c>Aggregate == true &amp;&amp; Type == Numeric</c>) and is not Total itself.
/// A Total × aggregate-component cell is the one the rest-score de-bias
/// corrects (slice 2).
/// </param>
/// <param name="IsTotal">
/// True for the Total series (a Score named "Total", case-insensitive, with no
/// Index). Replaces the old "Total is the last series" positional assumption;
/// the red Total styling keys off this flag.
/// </param>
public record CorrelationColumnInfo(
    ScoreColumnType Type,
    bool IsAggregateComponent,
    bool IsTotal);
