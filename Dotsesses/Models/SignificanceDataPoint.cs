namespace Dotsesses.Models;

/// <summary>
/// One dot in the Significance Matrix plot. Each dot represents a
/// <c>Subgroup</c> of a Categorical column averaged against a Numeric column
/// (see CONTEXT.md). Unlike <see cref="CorrelationDataPoint"/>, the dot does
/// not represent an individual student — it summarises the subgroup's
/// students. Slice 4 (ADR-0014) layers in the inferential test: every dot
/// in a cell carries that cell's omnibus <see cref="PValue"/> (null when the
/// cell is untestable) and a flag for whether its subgroup was
/// <see cref="Excluded"/> from the test for being too small (N&lt;2).
///
/// ADR-0018 adds <see cref="EffectSize"/> — the cell's variance-explained
/// effect size (η² parametric / ε² non-parametric) on a 0–1 scale, comparable
/// to r² on the correlation tab. It is the headline; p is supporting detail.
/// Also repeated on every dot in the cell; null when untestable.
/// </summary>
public record SignificanceDataPoint(
    int CellRow,
    int CellCol,
    double X,
    double Y,
    string CategoricalColumn,
    string NumericColumn,
    string Subgroup,
    double Mean,
    double Sem,
    int N,
    string Color,
    double? PValue = null,
    double? EffectSize = null,
    SignificanceTestFamily TestFamily = SignificanceTestFamily.Parametric,
    bool Excluded = false);
