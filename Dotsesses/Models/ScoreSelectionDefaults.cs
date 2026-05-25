namespace Dotsesses.Models;

/// <summary>
/// Generates default <see cref="ScoreSelection"/> values for a set of <see cref="Score"/>s and
/// <see cref="StudentAttribute"/>s.
///
/// Numeric defaults: Display=true and Correlation=true; Aggregate=true except for "Total"
/// (case-insensitive), which is excluded so the aggregate is not double-counted with a
/// pre-summed Total column.
///
/// Categorical defaults: Display=true (so the drill-down's Attributes section is populated),
/// Aggregate=false, Correlation=false — those flags are meaningless for categorical columns.
/// </summary>
public static class ScoreSelectionDefaults
{
    /// <summary>
    /// Returns the default selection set for the supplied scores and attributes. Does not
    /// mutate the inputs. Numeric selections are emitted in the order of <paramref name="scores"/>
    /// followed by Categorical selections in the order of <paramref name="attributes"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when either input is null.</exception>
    public static IReadOnlyList<ScoreSelection> GenerateDefaults(
        IReadOnlyCollection<Score> scores,
        IReadOnlyCollection<StudentAttribute> attributes)
    {
        ArgumentNullException.ThrowIfNull(scores);
        ArgumentNullException.ThrowIfNull(attributes);

        var result = new List<ScoreSelection>(scores.Count + attributes.Count);

        foreach (var score in scores)
        {
            var isTotalColumn = string.Equals(score.Name, "Total", StringComparison.OrdinalIgnoreCase);
            result.Add(new ScoreSelection(
                Name: score.Name,
                Index: score.Index,
                Type: ScoreColumnType.Numeric,
                Display: true,
                Aggregate: !isTotalColumn,
                Correlation: true));
        }

        foreach (var attribute in attributes)
        {
            result.Add(new ScoreSelection(
                Name: attribute.Name,
                Index: attribute.Index,
                Type: ScoreColumnType.Categorical,
                Display: true,
                Aggregate: false,
                Correlation: false));
        }

        return result;
    }

    /// <summary>
    /// Convenience overload for the common "no attributes" case (used by tests and pre-categorical
    /// code paths). Delegates to the two-argument overload with an empty attribute list.
    /// </summary>
    public static IReadOnlyList<ScoreSelection> GenerateDefaults(IReadOnlyCollection<Score> scores) =>
        GenerateDefaults(scores, Array.Empty<StudentAttribute>());
}
