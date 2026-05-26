namespace Dotsesses.Models;

/// <summary>
/// Whether a score column carries numeric (<see cref="Score"/>) or
/// categorical (<see cref="StudentAttribute"/>) data. Numeric columns
/// participate in plots and AggregateScore; Categorical columns appear
/// only in the drill-down's Attributes section and the (planned)
/// color-by-attribute selector.
/// </summary>
public enum ScoreColumnType
{
    Numeric,
    Categorical,
}

/// <summary>
/// Represents a user selection for a score column controlling its type and per-plot inclusion flags.
/// </summary>
/// <param name="Name">Column name (matches <see cref="Score.Name"/> or <see cref="StudentAttribute.Name"/>)</param>
/// <param name="Index">Optional index for multiple columns of same name</param>
/// <param name="Type">Whether the column is Numeric or Categorical. Display/Aggregate/Correlation are meaningless when Categorical.</param>
/// <param name="Display">Whether this score is shown in the violin plot</param>
/// <param name="Aggregate">Whether this score contributes to the computed aggregate</param>
/// <param name="Correlation">Whether this score participates in the correlation matrix</param>
/// <param name="Significance">Whether this column participates in the Significance Matrix (Numeric rows / Categorical columns). Unlike the other flags, meaningful for both Types.</param>
public record ScoreSelection(string Name, int? Index, ScoreColumnType Type, bool Display, bool Aggregate, bool Correlation, bool Significance = true);
