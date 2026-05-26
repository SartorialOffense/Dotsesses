namespace Dotsesses.Models;

/// <summary>
/// One dot in the Significance Matrix plot. Each dot represents a
/// <c>Subgroup</c> of a Categorical column averaged against a Numeric column
/// (see CONTEXT.md). Unlike <see cref="CorrelationDataPoint"/>, the dot does
/// not represent an individual student — it summarises the subgroup's
/// students. The point shape leaves room for a future per-cell <c>PValue</c>
/// field when slice 4 layers in the inferential test (see ADR-0014).
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
    string Color);
