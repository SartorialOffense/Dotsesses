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
