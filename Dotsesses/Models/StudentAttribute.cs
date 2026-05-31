namespace Dotsesses.Models;

/// <summary>
/// Represents a non-numeric student attribute. Carries an optional Comment so that
/// per-cell comments survive a Numeric→Categorical type switch and reappear on a
/// Categorical→Numeric reverse (see ADR-0013). ADR-0007 still applies — comments are
/// per-column-per-student data, not a single Student-level field.
/// </summary>
/// <param name="Name">Attribute name (e.g., "Submitted Outline", "Mid-Term")</param>
/// <param name="Index">Optional index for multiple attributes of same type</param>
/// <param name="Value">Attribute value with any <c>~N</c> sort-order suffix stripped (e.g., "Yes", "No", "✔✔+").</param>
/// <param name="Comment">Optional comment carried over from a converted <see cref="Score"/>, or null otherwise.</param>
/// <param name="SortOrder">
/// Optional non-negative rank decoded at load time from a trailing <c>~N</c> suffix on the
/// cell value (<c>Pass~2</c> → Value "Pass", SortOrder 2); null when the cell had no valid
/// suffix. Orders the value among its column's Subgroups in the Significance Matrix, and —
/// when *every* cell of the column is suffixed — supplies the column's numeric value as an
/// <see cref="ScoreColumnType.Ordinal"/> column. See ADR-0017.
/// </param>
public record StudentAttribute(string Name, int? Index, string Value, string? Comment = null, int? SortOrder = null);
