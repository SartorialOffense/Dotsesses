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
        string aggregateScoreName = "Total")
    {
        ArgumentNullException.ThrowIfNull(scores);
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentNullException.ThrowIfNull(muppetName);
        ArgumentNullException.ThrowIfNull(aggregateScoreName);

        Id = id;
        Scores = scores.ToList();
        Attributes = attributes.ToList();
        MuppetName = muppetName;

        // Look up aggregate grade by name (default "Total")
        RecalculateAggregate(aggregateScoreName);
    }

    /// <summary>
    /// Recalculates the aggregate grade from scores.
    /// </summary>
    public void RecalculateAggregate(string aggregateScoreName = "Total")
    {
        var aggregateScore = Scores.FirstOrDefault(s => s.Name == aggregateScoreName);
        _aggregateGrade = aggregateScore != null ? (int)aggregateScore.Value : 0;
    }
}
