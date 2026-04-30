namespace Dotsesses.Tests.Models;

using Dotsesses.Models;

public class StudentAssessmentTests
{
    private static List<StudentAttribute> NoAttributes() => new();

    [Fact]
    public void Constructor_WithoutSelection_DefaultsToTotalColumn()
    {
        var scores = new List<Score>
        {
            new("Q#", 1, 10),
            new("Q#", 2, 20),
            new("Total", null, 30)
        };

        var sa = new StudentAssessment(1, scores, NoAttributes(), "Kermit");

        Assert.Equal(30, sa.AggregateGrade);
    }

    [Fact]
    public void Constructor_NoTotalScore_AggregateGradeIsZero()
    {
        var scores = new List<Score>
        {
            new("Q#", 1, 10),
            new("Q#", 2, 20)
        };

        var sa = new StudentAssessment(1, scores, NoAttributes(), "Kermit");

        Assert.Equal(0, sa.AggregateGrade);
    }

    [Fact]
    public void RecalculateAggregate_WithSelectionSet_SumsSelectedScores()
    {
        var scores = new List<Score>
        {
            new("Q", 1, 10),
            new("Q", 2, 20),
            new("Total", null, 30)
        };
        var sa = new StudentAssessment(1, scores, NoAttributes(), "Kermit");

        sa.RecalculateAggregate(new[] { ("Q", (int?)1), ("Q", (int?)2) });

        Assert.Equal(30, sa.AggregateGrade);
    }

    [Fact]
    public void RecalculateAggregate_ExcludesUnselectedScores()
    {
        var scores = new List<Score>
        {
            new("Q", 1, 10),
            new("Q", 2, 20),
            new("Total", null, 30)
        };
        var sa = new StudentAssessment(1, scores, NoAttributes(), "Kermit");

        sa.RecalculateAggregate(new[] { ("Q", (int?)1) });

        Assert.Equal(10, sa.AggregateGrade);
    }

    [Fact]
    public void RecalculateAggregate_NullSelection_FallsBackToTotal()
    {
        var scores = new List<Score>
        {
            new("Q#", 1, 10),
            new("Total", null, 42)
        };
        var sa = new StudentAssessment(1, scores, NoAttributes(), "Kermit");

        sa.RecalculateAggregate(null);

        Assert.Equal(42, sa.AggregateGrade);
    }

    [Fact]
    public void RecalculateAggregate_EmptySelection_ReturnsZero()
    {
        var scores = new List<Score>
        {
            new("Total", null, 30)
        };
        var sa = new StudentAssessment(1, scores, NoAttributes(), "Kermit");

        sa.RecalculateAggregate(Array.Empty<(string, int?)>());

        Assert.Equal(0, sa.AggregateGrade);
    }

    [Fact]
    public void RecalculateAggregate_SumThenTruncate()
    {
        var scores = new List<Score>
        {
            new("A", null, 2.7),
            new("B", null, 2.7)
        };
        var sa = new StudentAssessment(1, scores, NoAttributes(), "Kermit");

        sa.RecalculateAggregate(new[] { ("A", (int?)null), ("B", (int?)null) });

        // Sum-then-cast: 2.7 + 2.7 = 5.4 -> 5. Per-score truncation would give 2 + 2 = 4.
        Assert.Equal(5, sa.AggregateGrade);
    }

    [Fact]
    public void RecalculateAggregate_NameCaseSensitive()
    {
        var scores = new List<Score>
        {
            new("Total", null, 30)
        };
        var sa = new StudentAssessment(1, scores, NoAttributes(), "Kermit");

        sa.RecalculateAggregate(new[] { ("total", (int?)null) });

        Assert.Equal(0, sa.AggregateGrade);
    }
}
