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
    SignificanceTestFamily TestFamily = SignificanceTestFamily.Parametric,
    bool Excluded = false);
