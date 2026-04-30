namespace Dotsesses.Models;

/// <summary>
/// Generates default <see cref="ScoreSelection"/> values for a set of <see cref="Score"/>s.
/// Defaults: Display=true and Correlation=true for all scores; Aggregate=true for all
/// except a column named "Total" (case-insensitive), which is excluded so the aggregate
/// is not double-counted with a pre-summed Total column.
/// </summary>
public static class ScoreSelectionDefaults
{
    /// <summary>
    /// Returns the default selection set for the supplied scores. Does not mutate the input.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scores"/> is null.</exception>
    public static IReadOnlyList<ScoreSelection> GenerateDefaults(IReadOnlyCollection<Score> scores)
    {
        ArgumentNullException.ThrowIfNull(scores);

        return scores
            .Select(score =>
            {
                var isTotalColumn = string.Equals(score.Name, "Total", StringComparison.OrdinalIgnoreCase);
                return new ScoreSelection(
                    Name: score.Name,
                    Index: score.Index,
                    Display: true,
                    Aggregate: !isTotalColumn,
                    Correlation: true);
            })
            .ToList();
    }
}
