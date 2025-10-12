namespace Dotsesses.Models;

/// <summary>
/// Represents a single student's assessment data.
/// </summary>
public class StudentAssessment
{
    private readonly int _aggregateGrade;

    public int Id { get; }
    public IReadOnlyCollection<Score> Scores { get; }
    public IReadOnlyCollection<StudentAttribute> Attributes { get; }
    public string MuppetName { get; }

    /// <summary>
    /// Optional multiline comment for this student.
    /// </summary>
    public string? Comment { get; set; }

    /// <summary>
    /// Cached aggregate grade calculated on construction.
    /// </summary>
    public int AggregateGrade => _aggregateGrade;

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
        Scores = scores;
        Attributes = attributes;
        MuppetName = muppetName;

        // Look up aggregate grade by name (default "Total")
        var aggregateScore = scores.FirstOrDefault(s => s.Name == aggregateScoreName);
        _aggregateGrade = aggregateScore != null ? (int)aggregateScore.Value : 0;
    }
}
