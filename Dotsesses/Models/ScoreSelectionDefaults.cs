namespace Dotsesses.Models;

/// <summary>
/// Generates default <see cref="ScoreSelection"/> values for a set of <see cref="Score"/>s and
/// <see cref="StudentAttribute"/>s.
///
/// Numeric defaults: Display=true and Correlation=true; Aggregate=true except for "Total"
/// (case-insensitive), which is excluded so the aggregate is not double-counted with a
/// pre-summed Total column. BiasCorrect (correlation rest-score de-bias) seeds on only for
/// numeric columns *before* the Total column in sheet order — the components that roll up
/// into Total; Total and any column after it seed off (ADR-0018). Independently toggleable.
///
/// Categorical defaults: Display=true (so the drill-down's Attributes section is populated),
/// Aggregate=false, Correlation=false — those flags are meaningless for categorical columns.
///
/// Ordinal defaults (ADR-0017): a categorical column whose every cell carries a <c>~N</c>
/// suffix. Aggregate=false (an Ordinal's N is a rank, never summed into AggregateScore) and
/// Display=false (Ordinal violins are opt-in, not seeded on at load); Correlation=false here,
/// with actual violin / correlation participation wired in a later slice. Significance=true.
/// </summary>
public static class ScoreSelectionDefaults
{
    /// <summary>
    /// Returns the default selection set for the supplied scores and attributes. Does not
    /// mutate the inputs. Numeric selections are emitted in the order of <paramref name="scores"/>
    /// followed by Categorical/Ordinal selections in the order of <paramref name="attributes"/>.
    /// An attribute column is typed <see cref="ScoreColumnType.Ordinal"/> when its
    /// <c>(Name, Index)</c> appears in <paramref name="ordinalColumns"/>, else Categorical.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when either required input is null.</exception>
    public static IReadOnlyList<ScoreSelection> GenerateDefaults(
        IReadOnlyCollection<Score> scores,
        IReadOnlyCollection<StudentAttribute> attributes,
        IReadOnlySet<(string Name, int? Index)>? ordinalColumns = null)
    {
        ArgumentNullException.ThrowIfNull(scores);
        ArgumentNullException.ThrowIfNull(attributes);

        var result = new List<ScoreSelection>(scores.Count + attributes.Count);

        // De-bias (BiasCorrect) seeds on only for numeric columns that appear
        // *before* the Total column in sheet order (ADR-0018): those are the
        // components that roll up into Total. Total itself and any column after
        // it (curved score, percentile, …) seed off. `passedTotal` flips once we
        // reach Total; if there's no real Total column, ScoreReader appends a
        // synthesized one last, so every real numeric correctly seeds on.
        var passedTotal = false;
        foreach (var score in scores)
        {
            var isTotalColumn = string.Equals(score.Name, "Total", StringComparison.OrdinalIgnoreCase);
            result.Add(new ScoreSelection(
                Name: score.Name,
                Index: score.Index,
                Type: ScoreColumnType.Numeric,
                Display: true,
                Aggregate: !isTotalColumn,
                Correlation: true,
                Significance: true,
                BiasCorrect: !passedTotal && !isTotalColumn));
            if (isTotalColumn)
            {
                passedTotal = true;
            }
        }

        foreach (var attribute in attributes)
        {
            var isOrdinal = ordinalColumns?.Contains((attribute.Name, attribute.Index)) == true;
            result.Add(new ScoreSelection(
                Name: attribute.Name,
                Index: attribute.Index,
                Type: isOrdinal ? ScoreColumnType.Ordinal : ScoreColumnType.Categorical,
                // Ordinal violins are opt-in; plain Categorical seeds Display on so the
                // drill-down's Attributes section is populated.
                Display: !isOrdinal,
                Aggregate: false,
                Correlation: false,
                Significance: true));
        }

        return result;
    }

    /// <summary>
    /// Convenience overload for the common "no attributes" case (used by tests and pre-categorical
    /// code paths). Delegates to the main overload with an empty attribute list.
    /// </summary>
    public static IReadOnlyList<ScoreSelection> GenerateDefaults(IReadOnlyCollection<Score> scores) =>
        GenerateDefaults(scores, Array.Empty<StudentAttribute>());

    /// <summary>
    /// Determines which attribute columns qualify as <see cref="ScoreColumnType.Ordinal"/>
    /// across all students (ADR-0017): a column is Ordinal iff every present (non-empty)
    /// cell carries a SortOrder. Sparse columns still qualify — the rule is over present
    /// cells, and absent cells are simply not in a student's Attributes list. Returns the
    /// set of qualifying <c>(Name, Index)</c> keys.
    /// </summary>
    public static IReadOnlySet<(string Name, int? Index)> DetectOrdinalColumns(
        IEnumerable<StudentAssessment> assessments)
    {
        ArgumentNullException.ThrowIfNull(assessments);

        return assessments
            .SelectMany(a => a.Attributes)
            .GroupBy(a => (a.Name, a.Index))
            .Where(g => g.All(a => a.SortOrder.HasValue))
            .Select(g => g.Key)
            .ToHashSet();
    }
}
