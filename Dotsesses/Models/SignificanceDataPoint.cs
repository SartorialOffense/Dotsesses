namespace Dotsesses.Models;

/// <summary>
/// One point in the Significance Matrix plot. As of ADR-0019 each cell shows a
/// box plot + jittered points per Subgroup, so a point now represents an
/// **individual student's score** (<see cref="StudentId"/> / <see cref="Value"/>),
/// not a subgroup mean — the box (drawn in the SVG) conveys median/IQR/whiskers.
/// <see cref="Subgroup"/> and <see cref="N"/> (the student's subgroup size) carry
/// the group context for the hover tooltip.
///
/// The inferential fields are cell-level and repeated on every point in the cell:
/// the omnibus <see cref="PValue"/> (null when untestable), the variance-explained
/// <see cref="EffectSize"/> (η² parametric / ε² non-parametric, null when
/// untestable — ADR-0018), the <see cref="TestFamily"/>, and <see cref="Excluded"/>
/// (true when the student's subgroup was dropped from the test for N&lt;2).
/// </summary>
public record SignificanceDataPoint(
    int CellRow,
    int CellCol,
    double X,
    double Y,
    string CategoricalColumn,
    string NumericColumn,
    string Subgroup,
    int StudentId,
    double Value,
    int N,
    string Color,
    double? PValue = null,
    double? EffectSize = null,
    SignificanceTestFamily TestFamily = SignificanceTestFamily.Parametric,
    bool Excluded = false);
