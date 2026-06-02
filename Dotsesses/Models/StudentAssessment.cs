using System.Text.Json.Serialization;

namespace Dotsesses.Models;

/// <summary>
/// Represents a single student's assessment data.
/// </summary>
public class StudentAssessment
{
    private int _aggregateGrade;

    public int Id { get; set; }
    public List<Score> Scores { get; set; } = new();
    public List<StudentAttribute> Attributes { get; set; } = new();
    public string MuppetName { get; set; } = string.Empty;

    /// <summary>
    /// Cached aggregate grade. Call RecalculateAggregate() after modifying scores.
    /// </summary>
    public int AggregateGrade => _aggregateGrade;

    /// <summary>
    /// Parameterless constructor for JSON deserialization.
    /// </summary>
    [JsonConstructor]
    public StudentAssessment() { }

    public StudentAssessment(
        int id,
        IReadOnlyCollection<Score> scores,
        IReadOnlyCollection<StudentAttribute> attributes,
        string muppetName,
        IReadOnlyCollection<(string Name, int? Index)>? aggregateSelection = null)
    {
        ArgumentNullException.ThrowIfNull(scores);
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentNullException.ThrowIfNull(muppetName);

        Id = id;
        Scores = scores.ToList();
        Attributes = attributes.ToList();
        MuppetName = muppetName;

        RecalculateAggregate(aggregateSelection);
    }

    /// <summary>
    /// Recalculates the aggregate grade by summing scores whose (Name, Index) is in the selection set.
    /// A null selection falls back to the single "Total" Score, matched case-INSENSITIVELY (so a
    /// "TOTAL"/"total" column counts) — see the in-body comment for why the fallback differs from the
    /// case-sensitive explicit-selection match. Sum-then-truncate ordering preserves the v1
    /// single-score behavior on the typical case.
    /// <para>
    /// When the selection set excludes the "Total" Score (case-insensitive), this method also
    /// mirrors the computed aggregate sum into the Total Score's Value (preserving its Comment).
    /// This makes the spreadsheet's static "Total" column behave as a live mirror of the
    /// user-defined aggregate, so downstream consumers (violin plot's Total series, dotplot
    /// tooltip, drill-down panel) reflect aggregate-toggle changes without needing a separate
    /// derived-value channel. When the selection set INCLUDES Total (the null/default-Total
    /// fallback used during construction or v1-style aggregation), the Total Score is NOT
    /// mutated — that would be a self-referential overwrite.
    /// </para>
    /// </summary>
    public void RecalculateAggregate(IReadOnlyCollection<(string Name, int? Index)>? aggregateSelection = null)
    {
        // Null selection (construction-time / v1 fallback): the aggregate is the
        // single Total Score, matched case-INSENSITIVELY so an uppercase "TOTAL" or
        // "total" column satisfies the same contract as "Total". ScoreReader detects
        // the Total column case-insensitively (and therefore does NOT synthesize one),
        // so a case-sensitive match here would silently yield AggregateGrade = 0 for
        // every student and collapse the initial cursor layout into an out-of-order
        // state that GradeAssigner rejects. Explicit selection sets stay
        // case-SENSITIVE (MEM008): their keys are derived from the actual Scores, so
        // their casing already matches.
        if (aggregateSelection is null)
        {
            _aggregateGrade = (int)Scores
                .Where(s => s.Index == null &&
                            string.Equals(s.Name, "Total", StringComparison.OrdinalIgnoreCase))
                .Sum(s => s.Value);
            // Total is itself the aggregate, so there is nothing to mirror onto.
            return;
        }

        var keys = aggregateSelection.ToHashSet();
        var sum = Scores.Where(s => keys.Contains((s.Name, s.Index))).Sum(s => s.Value);
        _aggregateGrade = (int)sum;

        // Mirror the computed aggregate into the Total Score's Value when Total is NOT itself
        // an aggregate component. This keeps the visible "Total" series/column tied to the
        // user-defined aggregate set rather than the original (now-stale) spreadsheet value.
        var totalIsSelected = keys.Any(k =>
            string.Equals(k.Name, "Total", StringComparison.OrdinalIgnoreCase) && k.Index == null);
        if (!totalIsSelected)
        {
            var totalScore = Scores.FirstOrDefault(s =>
                string.Equals(s.Name, "Total", StringComparison.OrdinalIgnoreCase) && s.Index == null);
            if (totalScore != null)
            {
                totalScore.Value = sum;
            }
        }
    }
}
