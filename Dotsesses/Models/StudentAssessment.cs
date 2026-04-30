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
    /// A null selection falls back to <c>[("Total", null)]</c> for back-compat.
    /// Sum-then-truncate ordering preserves the v1 single-score behavior on the typical case.
    /// </summary>
    public void RecalculateAggregate(IReadOnlyCollection<(string Name, int? Index)>? aggregateSelection = null)
    {
        var selection = aggregateSelection ?? new[] { ("Total", (int?)null) };
        var keys = selection.ToHashSet();
        _aggregateGrade = (int)Scores.Where(s => keys.Contains((s.Name, s.Index))).Sum(s => s.Value);
    }
}
