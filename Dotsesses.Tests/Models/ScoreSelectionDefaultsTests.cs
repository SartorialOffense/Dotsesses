namespace Dotsesses.Tests.Models;

using Dotsesses.Models;

public class ScoreSelectionDefaultsTests
{
    [Fact]
    public void GenerateDefaults_WithTotalColumn_AggregateOffOnlyForTotal()
    {
        // Arrange
        var scores = new List<Score>
        {
            new("Q#", 1, 10),
            new("Q#", 2, 20),
            new("Total", null, 30)
        };

        // Act
        var result = ScoreSelectionDefaults.GenerateDefaults(scores);

        // Assert
        Assert.Equal(3, result.Count);
        Assert.All(result, s => Assert.True(s.Display));
        Assert.All(result, s => Assert.True(s.Correlation));

        var q1 = result.First(s => s.Name == "Q#" && s.Index == 1);
        var q2 = result.First(s => s.Name == "Q#" && s.Index == 2);
        var total = result.First(s => s.Name == "Total");

        Assert.True(q1.Aggregate);
        Assert.True(q2.Aggregate);
        Assert.False(total.Aggregate);
    }

    [Fact]
    public void GenerateDefaults_WithoutTotal_AggregateOnForAll()
    {
        // Arrange
        var scores = new List<Score>
        {
            new("Q#", 1, 10),
            new("Q#", 2, 20)
        };

        // Act
        var result = ScoreSelectionDefaults.GenerateDefaults(scores);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, s =>
        {
            Assert.True(s.Display);
            Assert.True(s.Aggregate);
            Assert.True(s.Correlation);
        });
    }

    [Theory]
    [InlineData("Total")]
    [InlineData("total")]
    [InlineData("TOTAL")]
    [InlineData("ToTaL")]
    public void GenerateDefaults_TotalCaseInsensitive(string totalName)
    {
        // Arrange
        var scores = new List<Score>
        {
            new("Q#", 1, 10),
            new(totalName, null, 30)
        };

        // Act
        var result = ScoreSelectionDefaults.GenerateDefaults(scores);

        // Assert
        var totalSelection = result.First(s =>
            string.Equals(s.Name, totalName, StringComparison.Ordinal));
        Assert.False(totalSelection.Aggregate);

        var q1 = result.First(s => s.Name == "Q#");
        Assert.True(q1.Aggregate);
    }

    [Fact]
    public void GenerateDefaults_EmptyInput_ReturnsEmpty()
    {
        // Arrange
        var scores = new List<Score>();

        // Act
        var result = ScoreSelectionDefaults.GenerateDefaults(scores);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GenerateDefaults_PreservesNameAndIndex()
    {
        // Arrange
        var scores = new List<Score>
        {
            new("Q", 1, 5),
            new("Q", 2, 7),
            new("Essay", null, 15)
        };

        // Act
        var result = ScoreSelectionDefaults.GenerateDefaults(scores);

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal("Q", result[0].Name);
        Assert.Equal(1, result[0].Index);
        Assert.Equal("Q", result[1].Name);
        Assert.Equal(2, result[1].Index);
        Assert.Equal("Essay", result[2].Name);
        Assert.Null(result[2].Index);
    }

    [Fact]
    public void GenerateDefaults_NullInput_Throws()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            ScoreSelectionDefaults.GenerateDefaults(null!));
    }

    [Fact]
    public void GenerateDefaults_WithCategoricalAttributes_EmitsCategoricalSelections()
    {
        // Arrange
        var scores = new List<Score>
        {
            new("Q#", 1, 10),
            new("Total", null, 30)
        };
        var attributes = new List<StudentAttribute>
        {
            new("Submitted Outline", null, "Yes")
        };

        // Act
        var result = ScoreSelectionDefaults.GenerateDefaults(scores, attributes);

        // Assert — Numeric rows come first, then Categorical rows.
        Assert.Equal(3, result.Count);

        var q1 = result.First(s => s.Name == "Q#");
        Assert.Equal(ScoreColumnType.Numeric, q1.Type);
        Assert.True(q1.Aggregate);

        var total = result.First(s => s.Name == "Total");
        Assert.Equal(ScoreColumnType.Numeric, total.Type);
        Assert.False(total.Aggregate);

        var outline = result.First(s => s.Name == "Submitted Outline");
        Assert.Equal(ScoreColumnType.Categorical, outline.Type);
        Assert.True(outline.Display);
        Assert.False(outline.Aggregate);
        Assert.False(outline.Correlation);
    }

    [Fact]
    public void GenerateDefaults_SetsSignificanceTrue_OnAllRows()
    {
        // Arrange — one Numeric, one Categorical, one Total. All three should
        // get Significance=true so the Significance Matrix populates on first load.
        var scores = new List<Score>
        {
            new("Q#", 1, 10),
            new("Total", null, 30)
        };
        var attributes = new List<StudentAttribute>
        {
            new("Submitted Outline", null, "Yes")
        };

        var result = ScoreSelectionDefaults.GenerateDefaults(scores, attributes);

        Assert.All(result, s => Assert.True(s.Significance,
            $"Significance should default true for {s.Name}"));
    }

    [Fact]
    public void GenerateDefaults_SeedsBiasCorrect_OnlyForColumnsBeforeTotal()
    {
        // ADR-0018: BiasCorrect (de-bias) seeds on only for numeric columns BEFORE
        // the Total column in sheet order — the components rolling up into Total.
        // Total and any column after it (e.g. a curved score) seed off.
        var scores = new List<Score>
        {
            new("Q1", null, 10),
            new("Exam", null, 20),
            new("Total", null, 30),
            new("Curved", null, 33),  // post-Total — must NOT be bias-corrected
        };
        var attributes = new List<StudentAttribute>
        {
            new("Hat", null, "Yes"),
        };

        var result = ScoreSelectionDefaults.GenerateDefaults(scores, attributes);

        Assert.True(result.First(s => s.Name == "Q1").BiasCorrect);      // before Total
        Assert.True(result.First(s => s.Name == "Exam").BiasCorrect);    // before Total
        Assert.False(result.First(s => s.Name == "Total").BiasCorrect);  // the target itself
        Assert.False(result.First(s => s.Name == "Curved").BiasCorrect); // after Total
        Assert.False(result.First(s => s.Name == "Hat").BiasCorrect);    // categorical
    }

    [Fact]
    public void DetectOrdinalColumns_AllCellsSuffixed_QualifiesAsOrdinal()
    {
        // ADR-0017: a column where every present cell carries a SortOrder is Ordinal.
        var assessments = new List<StudentAssessment>
        {
            new(1, new List<Score> { new("Total", null, 80) },
                new List<StudentAttribute> { new("Mid-Term", null, "✔", SortOrder: 1) }, "M1"),
            new(2, new List<Score> { new("Total", null, 70) },
                new List<StudentAttribute> { new("Mid-Term", null, "✔✔", SortOrder: 2) }, "M2"),
        };

        var ordinal = ScoreSelectionDefaults.DetectOrdinalColumns(assessments);

        Assert.Contains(("Mid-Term", (int?)null), ordinal);
    }

    [Fact]
    public void DetectOrdinalColumns_PartiallySuffixed_DoesNotQualify()
    {
        var assessments = new List<StudentAssessment>
        {
            new(1, new List<Score> { new("Total", null, 80) },
                new List<StudentAttribute> { new("Mid-Term", null, "Pass", SortOrder: 2) }, "M1"),
            new(2, new List<Score> { new("Total", null, 70) },
                new List<StudentAttribute> { new("Mid-Term", null, "Incomplete") }, "M2"),
        };

        var ordinal = ScoreSelectionDefaults.DetectOrdinalColumns(assessments);

        Assert.DoesNotContain(("Mid-Term", (int?)null), ordinal);
    }

    [Fact]
    public void DetectOrdinalColumns_SparseButAllPresentSuffixed_Qualifies()
    {
        // The rule is over PRESENT cells; an absent cell (student 2 has no Mid-Term)
        // does not disqualify the column.
        var assessments = new List<StudentAssessment>
        {
            new(1, new List<Score> { new("Total", null, 80) },
                new List<StudentAttribute> { new("Mid-Term", null, "✔", SortOrder: 1) }, "M1"),
            new(2, new List<Score> { new("Total", null, 70) },
                new List<StudentAttribute>(), "M2"),
        };

        var ordinal = ScoreSelectionDefaults.DetectOrdinalColumns(assessments);

        Assert.Contains(("Mid-Term", (int?)null), ordinal);
    }

    [Fact]
    public void GenerateDefaults_OrdinalColumn_TypedOrdinalDisplayOffAggregateOff()
    {
        var scores = new List<Score> { new("Total", null, 30) };
        var attributes = new List<StudentAttribute> { new("Mid-Term", null, "✔", SortOrder: 1) };
        var ordinalColumns = new HashSet<(string, int?)> { ("Mid-Term", null) };

        var result = ScoreSelectionDefaults.GenerateDefaults(scores, attributes, ordinalColumns);

        var midterm = result.First(s => s.Name == "Mid-Term");
        Assert.Equal(ScoreColumnType.Ordinal, midterm.Type);
        Assert.False(midterm.Display);     // Ordinal violins are opt-in
        Assert.False(midterm.Aggregate);   // N is a rank, never aggregated
        Assert.True(midterm.Significance);
    }

    [Fact]
    public void GenerateDefaults_AttributeNotInOrdinalSet_StaysCategorical()
    {
        var scores = new List<Score> { new("Total", null, 30) };
        var attributes = new List<StudentAttribute> { new("Submitted Outline", null, "Yes") };

        var result = ScoreSelectionDefaults.GenerateDefaults(
            scores, attributes, new HashSet<(string, int?)>());

        var outline = result.First(s => s.Name == "Submitted Outline");
        Assert.Equal(ScoreColumnType.Categorical, outline.Type);
        Assert.True(outline.Display);
    }

    [Fact]
    public void GenerateDefaults_NoAttributes_BehavesLikeOneArgOverload()
    {
        // Arrange — verify the convenience overload (just scores) matches the
        // explicit empty-attributes call.
        var scores = new List<Score>
        {
            new("Q#", 1, 10),
            new("Total", null, 30)
        };

        // Act
        var fromOverload = ScoreSelectionDefaults.GenerateDefaults(scores);
        var fromExplicit = ScoreSelectionDefaults.GenerateDefaults(scores, Array.Empty<StudentAttribute>());

        // Assert
        Assert.Equal(fromExplicit.Count, fromOverload.Count);
        for (int i = 0; i < fromOverload.Count; i++)
        {
            Assert.Equal(fromExplicit[i], fromOverload[i]);
        }
        Assert.All(fromOverload, s => Assert.Equal(ScoreColumnType.Numeric, s.Type));
    }
}
