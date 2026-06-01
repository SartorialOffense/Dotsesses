namespace Dotsesses.Models;

/// <summary>
/// Whether a score column carries numeric (<see cref="Score"/>) or
/// categorical (<see cref="StudentAttribute"/>) data. Numeric columns
/// participate in plots and AggregateScore; Categorical columns appear
/// only in the drill-down's Attributes section and the (planned)
/// color-by-attribute selector.
///
/// <see cref="Ordinal"/> is a categorical column in which *every* cell
/// carries a <c>~N</c> sort-order suffix, so it has both a label and a
/// numeric value. Its data still lives in <see cref="StudentAttribute"/>
/// (with <see cref="StudentAttribute.SortOrder"/> set), but unlike a plain
/// Categorical it can participate in the violin and correlation views
/// (using N as the value) while acting as a Significance Matrix *column*.
/// It never contributes to AggregateScore. See ADR-0017.
/// </summary>
public enum ScoreColumnType
{
    Numeric,
    Categorical,
    Ordinal,
}

/// <summary>
/// Represents a user selection for a score column controlling its type and per-plot inclusion flags.
/// </summary>
/// <param name="Name">Column name (matches <see cref="Score.Name"/> or <see cref="StudentAttribute.Name"/>)</param>
/// <param name="Index">Optional index for multiple columns of same name</param>
/// <param name="Type">Numeric, Categorical, or Ordinal. For Categorical all three of Display/Aggregate/Correlation are meaningless; for Ordinal, Display and Correlation are meaningful but Aggregate is not (an Ordinal's N is a rank, never summed into AggregateScore).</param>
/// <param name="Display">Whether this score is shown in the violin plot</param>
/// <param name="Aggregate">Whether this score contributes to the computed aggregate</param>
/// <param name="Correlation">Whether this score participates in the correlation matrix</param>
/// <param name="Significance">Whether this column participates in the Significance Matrix (Numeric rows / Categorical + Ordinal columns). Unlike the other flags, meaningful for all Types.</param>
/// <param name="BiasCorrect">Whether this column is rest-score de-biased against Total in the correlation matrix — i.e. correlated against <c>Total − this column</c> rather than the full Total (ADR-0018). The sole trigger for that correction, decoupled from <paramref name="Aggregate"/>: a composite column (e.g. <c>Q1-Q4 = Q1+Q2+Q3+Q4</c>) can be de-biased without being summed into the aggregate. Meaningful only for Numeric, non-Total columns. Seeded from <c>Numeric &amp;&amp; Aggregate &amp;&amp; !Total</c> at load, then independently toggleable.</param>
public record ScoreSelection(string Name, int? Index, ScoreColumnType Type, bool Display, bool Aggregate, bool Correlation, bool Significance = true, bool BiasCorrect = false);
