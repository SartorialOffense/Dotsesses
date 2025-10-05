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
    /// Cached aggregate grade calculated on construction.
    /// </summary>
    public int AggregateGrade => _aggregateGrade;

    public StudentAssessment(
        int id,
        IReadOnlyCollection<Score> scores,
        IReadOnlyCollection<StudentAttribute> attributes,
        string muppetName)
    {
        ArgumentNullException.ThrowIfNull(scores);
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentNullException.ThrowIfNull(muppetName);

        Id = id;
        Scores = scores;
        Attributes = attributes;
        MuppetName = muppetName;

        // Calculate and cache aggregate grade on construction
        _aggregateGrade = (int)scores.Sum(s => s.Value);
    }
}
