namespace Dotsesses.Tests.Calculators;

using Dotsesses.Calculators;
using Dotsesses.Models;

public class CursorValidationTests
{
    private readonly CursorValidation _validation = new();

    [Fact]
    public void ValidateMovement_WithNoOverlap_ReturnsProposedScore()
    {
        // Arrange
        var gradeToMove = new Grade(LetterGrade.B, 1);
        var cutoffs = new List<GradeCutoff>
        {
            new(new Grade(LetterGrade.A, 0), 280),
            new(gradeToMove, 250),
            new(new Grade(LetterGrade.C, 2), 200)
        };

        // Act - move B to 260 (valid position)
        var result = _validation.ValidateMovement(gradeToMove, 260, cutoffs);

        // Assert
        Assert.Equal(260, result);
    }

    [Fact]
    public void ValidateMovement_TooCloseToLowerCursor_SnapsToMinimumSpacing()
    {
        // Arrange
        var gradeToMove = new Grade(LetterGrade.B, 1);
        var cutoffs = new List<GradeCutoff>
        {
            new(new Grade(LetterGrade.A, 0), 280),
            new(gradeToMove, 250),
            new(new Grade(LetterGrade.C, 2), 200)
        };

        // Act - try to move B to 201 (only 1 point above C)
        var result = _validation.ValidateMovement(gradeToMove, 200, cutoffs);

        // Assert - should snap to 201 (minimum 1 point spacing)
        Assert.Equal(201, result);
    }

    [Fact]
    public void ValidateMovement_TooCloseToHigherCursor_SnapsToMinimumSpacing()
    {
        // Arrange
        var gradeToMove = new Grade(LetterGrade.B, 1);
        var cutoffs = new List<GradeCutoff>
        {
            new(new Grade(LetterGrade.A, 0), 280),
            new(gradeToMove, 250),
            new(new Grade(LetterGrade.C, 2), 200)
        };

        // Act - try to move B to 280 (overlapping with A)
        var result = _validation.ValidateMovement(gradeToMove, 280, cutoffs);

        // Assert - should snap to 279 (minimum 1 point below A)
        Assert.Equal(279, result);
    }

    [Fact]
    public void ValidateMovement_BetweenTwoCursors_EnforcesSpacing()
    {
        // Arrange
        var gradeToMove = new Grade(LetterGrade.B, 1);
        var cutoffs = new List<GradeCutoff>
        {
            new(new Grade(LetterGrade.A, 0), 205),  // Very close cursors
            new(gradeToMove, 203),
            new(new Grade(LetterGrade.C, 2), 201)
        };

        // Act - try to move to 202 (would be only 1 from C)
        var result = _validation.ValidateMovement(gradeToMove, 202, cutoffs);

        // Assert - should keep at valid position
        Assert.True(result >= 202 && result <= 204);
    }

    [Fact]
    public void IsValid_WithProperSpacing_ReturnsTrue()
    {
        // Arrange
        var cutoffs = new List<GradeCutoff>
        {
            new(new Grade(LetterGrade.A, 0), 280),
            new(new Grade(LetterGrade.B, 1), 250),
            new(new Grade(LetterGrade.C, 2), 200)
        };

        // Act
        var result = _validation.IsValid(cutoffs);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsValid_WithOverlap_ReturnsFalse()
    {
        // Arrange
        var cutoffs = new List<GradeCutoff>
        {
            new(new Grade(LetterGrade.A, 0), 280),
            new(new Grade(LetterGrade.B, 1), 280),  // Same score as A
            new(new Grade(LetterGrade.C, 2), 200)
        };

        // Act
        var result = _validation.IsValid(cutoffs);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValid_WithMinimumSpacing_ReturnsTrue()
    {
        // Arrange - exactly 1 point apart
        var cutoffs = new List<GradeCutoff>
        {
            new(new Grade(LetterGrade.A, 0), 203),
            new(new Grade(LetterGrade.B, 1), 202),
            new(new Grade(LetterGrade.C, 2), 201)
        };

        // Act
        var result = _validation.IsValid(cutoffs);

        // Assert
        Assert.True(result);
    }
}
