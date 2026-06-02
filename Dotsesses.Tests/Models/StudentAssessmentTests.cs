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
    public void Constructor_WithUppercaseTotalColumn_AggregatesCaseInsensitively()
    {
        // ScoreReader detects the Total column case-insensitively, so a gradebook
        // whose aggregate column is "TOTAL" never gets a synthesized "Total". The
        // null/default aggregate must still pick it up — otherwise every student's
        // AggregateGrade is 0 and the initial cursor layout collapses.
        var scores = new List<Score>
        {
            new("Exam", null, 40),
            new("TOTAL", null, 175)
        };

        var sa = new StudentAssessment(1, scores, NoAttributes(), "Kermit");

        Assert.Equal(175, sa.AggregateGrade);
    }

    [Fact]
    public void RecalculateAggregate_NullSelection_MatchesTotalCaseInsensitively()
    {
        var scores = new List<Score>
        {
            new("Q#", 1, 10),
            new("total", null, 88)
        };
        var sa = new StudentAssessment(1, scores, NoAttributes(), "Kermit");

        sa.RecalculateAggregate(null);

        Assert.Equal(88, sa.AggregateGrade);
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

    [Fact]
    public void RecalculateAggregate_MirrorsAggregateIntoTotalScoreValue_WhenTotalNotSelected()
    {
        // Spreadsheet may carry a static "Total" column whose value is independent of the
        // user-selected aggregate set. When the user toggles components on/off Aggregate,
        // the Total Score's Value must follow the new sum so consumers (violin "Total"
        // series, drill-down "Total" row) reflect the aggregate.
        var scores = new List<Score>
        {
            new("Q", 1, 10.0),
            new("Q", 2, 20.0),
            new("Q", 3, 5.0),
            new("Total", null, 50.0)  // stale spreadsheet value (intentionally wrong)
        };
        var sa = new StudentAssessment(1, scores, NoAttributes(), "Kermit");

        // Aggregate set excludes Total (the realistic case after defaults seeding).
        sa.RecalculateAggregate(new[] { ("Q", (int?)1), ("Q", (int?)2), ("Q", (int?)3) });

        // AggregateGrade is the truncated sum.
        Assert.Equal(35, sa.AggregateGrade);

        // Total Score's Value was mirrored to the actual sum (preserving precision).
        var totalScore = scores.First(s => s.Name == "Total");
        Assert.Equal(35.0, totalScore.Value);
    }

    [Fact]
    public void RecalculateAggregate_DoesNotMirror_WhenTotalIsItselfTheAggregate()
    {
        // When the selection set includes Total (e.g. the null fallback at construction
        // time, or the deliberate "Total only" v1-style aggregation), mirroring would be
        // a self-referential overwrite. The Total Score's Value must NOT be mutated.
        var scores = new List<Score>
        {
            new("Q", 1, 10.0),
            new("Total", null, 99.0)
        };
        var sa = new StudentAssessment(1, scores, NoAttributes(), "Kermit");

        // Null selection -> falls back to [("Total", null)].
        sa.RecalculateAggregate(null);

        // Aggregate equals Total's value.
        Assert.Equal(99, sa.AggregateGrade);

        // Total Score's Value is unchanged.
        var totalScore = scores.First(s => s.Name == "Total");
        Assert.Equal(99.0, totalScore.Value);
    }

    [Fact]
    public void RecalculateAggregate_TotalOnlySelection_TakesSpreadsheetTotalNotComponentSum()
    {
        // TD010 spreadsheet-Total mode: the aggregate set is exactly {Total}, so
        // AggregateGrade must equal the spreadsheet Total (100) — NOT the sum of
        // the components (70) — and Total's Value is left untouched.
        var scores = new List<Score>
        {
            new("Q", 1, 40.0),
            new("Q", 2, 30.0),
            new("Total", null, 100.0)  // deliberately ≠ component sum (70)
        };
        var sa = new StudentAssessment(1, scores, NoAttributes(), "Kermit");

        sa.RecalculateAggregate(new[] { ("Total", (int?)null) });

        Assert.Equal(100, sa.AggregateGrade);
        Assert.Equal(100.0, scores.First(s => s.Name == "Total").Value);
    }

    [Fact]
    public void RecalculateAggregate_PreservesTotalScoreComment_WhenMirroring()
    {
        // Total Score carries the student-level comment in this codebase. Mirroring
        // must update Value only, never touch Comment.
        var scores = new List<Score>
        {
            new("Q", 1, 10.0),
            new("Q", 2, 20.0),
            new("Total", null, 99.0, comment: "great work overall")
        };
        var sa = new StudentAssessment(1, scores, NoAttributes(), "Kermit");

        sa.RecalculateAggregate(new[] { ("Q", (int?)1), ("Q", (int?)2) });

        var totalScore = scores.First(s => s.Name == "Total");
        Assert.Equal(30.0, totalScore.Value);
        Assert.Equal("great work overall", totalScore.Comment);
    }

    [Fact]
    public void RecalculateAggregate_NoTotalScore_DoesNothingExtra()
    {
        // Defensive: if the data doesn't include a Total Score (some imports don't),
        // mirroring is silently skipped — no exception, AggregateGrade still set.
        var scores = new List<Score>
        {
            new("Q", 1, 10.0),
            new("Q", 2, 20.0)
        };
        var sa = new StudentAssessment(1, scores, NoAttributes(), "Kermit");

        sa.RecalculateAggregate(new[] { ("Q", (int?)1), ("Q", (int?)2) });

        Assert.Equal(30, sa.AggregateGrade);
        Assert.Equal(2, scores.Count);  // no synthetic Total inserted
    }
}
