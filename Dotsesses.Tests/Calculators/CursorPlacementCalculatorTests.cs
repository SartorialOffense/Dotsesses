namespace Dotsesses.Tests.Calculators;

using Dotsesses.Calculators;
using Dotsesses.Models;

public class CursorPlacementCalculatorTests
{
    private readonly CursorPlacementCalculator _calculator = new();

    [Fact]
    public void PlaceNewCursor_BetweenTwoCursors_PlacesAtMidpoint()
    {
        // Arrange
        var gradeToEnable = new Grade(LetterGrade.B, 1);
        var existingCutoffs = new List<GradeCutoff>
        {
            new(new Grade(LetterGrade.A, 0), 280),
            new(new Grade(LetterGrade.C, 2), 200)
        };

        // Act
        var result = _calculator.PlaceNewCursor(gradeToEnable, existingCutoffs, 100, 300);

        // Assert
        var newCutoff = result.First(r => r.Grade.LetterGrade == LetterGrade.B);
        Assert.Equal(240, newCutoff.Score);  // Midpoint of 280 and 200
    }

    [Fact]
    public void PlaceNewCursor_AtTopEdge_PlacesAboveLowestCursor()
    {
        // Arrange - enabling highest grade
        var gradeToEnable = new Grade(LetterGrade.A, 0);
        var existingCutoffs = new List<GradeCutoff>
        {
            new(new Grade(LetterGrade.B, 1), 250)
        };

        // Act
        var result = _calculator.PlaceNewCursor(gradeToEnable, existingCutoffs, 100, 300);

        // Assert
        var newCutoff = result.First(r => r.Grade.LetterGrade == LetterGrade.A);
        Assert.True(newCutoff.Score > 250);  // Above B
        Assert.True(newCutoff.Score <= 300);  // Within max score
    }

    [Fact]
    public void PlaceNewCursor_AtBottomEdge_PlacesBelowHighestCursor()
    {
        // Arrange - enabling lowest grade
        var gradeToEnable = new Grade(LetterGrade.C, 2);
        var existingCutoffs = new List<GradeCutoff>
        {
            new(new Grade(LetterGrade.B, 1), 250)
        };

        // Act
        var result = _calculator.PlaceNewCursor(gradeToEnable, existingCutoffs, 100, 300);

        // Assert
        var newCutoff = result.First(r => r.Grade.LetterGrade == LetterGrade.C);
        Assert.True(newCutoff.Score < 250);  // Below B
        Assert.True(newCutoff.Score >= 100);  // Within min score
    }

    [Fact]
    public void PlaceNewCursor_FirstCursor_PlacesAtMidpointOfRange()
    {
        // Arrange
        var gradeToEnable = new Grade(LetterGrade.A, 0);
        var existingCutoffs = new List<GradeCutoff>();

        // Act
        var result = _calculator.PlaceNewCursor(gradeToEnable, existingCutoffs, 100, 300);

        // Assert
        var newCutoff = result.First();
        Assert.Equal(200, newCutoff.Score);  // Midpoint of 100-300
    }

    [Fact]
    public void PlaceNewCursor_CausesOverlap_ResetsAllToEvenSpacing()
    {
        // Arrange - very close cursors
        var gradeToEnable = new Grade(LetterGrade.B, 1);
        var existingCutoffs = new List<GradeCutoff>
        {
            new(new Grade(LetterGrade.A, 0), 201),
            new(new Grade(LetterGrade.C, 2), 200)
        };

        // Act - midpoint would be 200.5, causing overlap
        var result = _calculator.PlaceNewCursor(gradeToEnable, existingCutoffs, 100, 300);

        // Assert - all three should be evenly spaced
        var sorted = result.OrderByDescending(r => r.Score).ToList();
        Assert.Equal(3, result.Count);

        // Check that spacing is roughly even
        int spacing1 = sorted[0].Score - sorted[1].Score;
        int spacing2 = sorted[1].Score - sorted[2].Score;
        Assert.True(Math.Abs(spacing1 - spacing2) <= 1);  // Allow for rounding
    }

    [Fact]
    public void PlaceNewCursor_MultipleGrades_MaintainsAllExisting()
    {
        // Arrange
        var gradeToEnable = new Grade(LetterGrade.AMinus, 1);
        var existingCutoffs = new List<GradeCutoff>
        {
            new(new Grade(LetterGrade.A, 0), 280),
            new(new Grade(LetterGrade.BPlus, 2), 260),
            new(new Grade(LetterGrade.B, 3), 240)
        };

        // Act
        var result = _calculator.PlaceNewCursor(gradeToEnable, existingCutoffs, 100, 300);

        // Assert
        Assert.Equal(4, result.Count);  // All 4 grades present
        Assert.Contains(result, r => r.Grade.LetterGrade == LetterGrade.A);
        Assert.Contains(result, r => r.Grade.LetterGrade == LetterGrade.AMinus);
        Assert.Contains(result, r => r.Grade.LetterGrade == LetterGrade.BPlus);
        Assert.Contains(result, r => r.Grade.LetterGrade == LetterGrade.B);
    }
}
