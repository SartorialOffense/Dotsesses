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
        var result = _validation.ValidateMovement(gradeToMove, 260, cutoffs, 0, 500);

        // Assert
        Assert.Equal(260, result);
    }

    [Fact]
    public void ValidateMovement_TooCloseToLowerCursor_SnapsToMinimumSpacing()
    {
        // Arrange - B is moving, C+ is below (not lowest grade), D is lowest (catch-all)
        var gradeToMove = new Grade(LetterGrade.B, 1);
        var cutoffs = new List<GradeCutoff>
        {
            new(new Grade(LetterGrade.A, 0), 280),
            new(gradeToMove, 250),
            new(new Grade(LetterGrade.CPlus, 3), 200),  // Not lowest grade
            new(new Grade(LetterGrade.D, 6), 150)        // Lowest grade (catch-all)
        };

        // Act - try to move B to 200 (overlapping with C+)
        var result = _validation.ValidateMovement(gradeToMove, 200, cutoffs, 0, 500);

        // Assert - should snap to 201 (minimum 1 point spacing above C+)
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
        var result = _validation.ValidateMovement(gradeToMove, 280, cutoffs, 0, 500);

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
        var result = _validation.ValidateMovement(gradeToMove, 202, cutoffs, 0, 500);

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

    [Fact]
    public void ValidateMovement_SecondToLastCursor_CanMoveToMinimumScore()
    {
        // Arrange - B+ is second-to-last (C is last/catch-all)
        // B+ should be able to move all the way down to min score
        var gradeToMove = new Grade(LetterGrade.BPlus, 2);
        var cutoffs = new List<GradeCutoff>
        {
            new(new Grade(LetterGrade.A, 0), 280),
            new(gradeToMove, 200),
            new(new Grade(LetterGrade.C, 4), 150)  // Lowest grade (catch-all)
        };

        // Act - try to move B+ down to 100 (below C's cursor)
        var result = _validation.ValidateMovement(gradeToMove, 100, cutoffs, 0, 500);

        // Assert - should allow movement to 100, not be blocked by C
        Assert.Equal(100, result);
    }

    [Fact]
    public void ValidateMovement_MiddleCursor_ConstrainedByBothNeighbors()
    {
        // Arrange - B is a middle cursor, not second-to-last
        var gradeToMove = new Grade(LetterGrade.B, 1);
        var cutoffs = new List<GradeCutoff>
        {
            new(new Grade(LetterGrade.A, 0), 280),
            new(gradeToMove, 250),
            new(new Grade(LetterGrade.BPlus, 2), 200),
            new(new Grade(LetterGrade.C, 4), 150)  // Lowest grade
        };

        // Act - try to move B down to 199 (too close to B+)
        var result = _validation.ValidateMovement(gradeToMove, 199, cutoffs, 0, 500);

        // Assert - should snap to 201 (minimum spacing above B+)
        Assert.Equal(201, result);
    }

    [Fact]
    public void ValidateMovement_TopCursor_OnlyConstrainedBelow()
    {
        // Arrange - A is the top cursor
        var gradeToMove = new Grade(LetterGrade.A, 0);
        var cutoffs = new List<GradeCutoff>
        {
            new(gradeToMove, 280),
            new(new Grade(LetterGrade.B, 1), 250),
            new(new Grade(LetterGrade.C, 2), 200)
        };

        // Act - try to move A to 1000
        var result = _validation.ValidateMovement(gradeToMove, 1000, cutoffs, 0, 2000);

        // Assert - should allow (no upper constraint)
        Assert.Equal(1000, result);
    }

    [Fact]
    public void ValidateMovement_SecondToLastCursor_StillConstrainedAbove()
    {
        // Arrange - B+ is second-to-last
        var gradeToMove = new Grade(LetterGrade.BPlus, 2);
        var cutoffs = new List<GradeCutoff>
        {
            new(new Grade(LetterGrade.A, 0), 280),
            new(new Grade(LetterGrade.B, 1), 250),
            new(gradeToMove, 200),
            new(new Grade(LetterGrade.C, 4), 150)
        };

        // Act - try to move B+ up to 250 (overlapping with B)
        var result = _validation.ValidateMovement(gradeToMove, 250, cutoffs, 0, 500);

        // Assert - should snap to 249 (minimum spacing below B)
        Assert.Equal(249, result);
    }

    [Fact]
    public void ValidateMovement_BelowMinBound_ClampedToMinBound()
    {
        // Arrange
        var gradeToMove = new Grade(LetterGrade.BPlus, 2);
        var cutoffs = new List<GradeCutoff>
        {
            new(new Grade(LetterGrade.A, 0), 280),
            new(gradeToMove, 200),
            new(new Grade(LetterGrade.C, 4), 150)
        };

        // Act - try to move B+ to -50 (below min bound of 0)
        var result = _validation.ValidateMovement(gradeToMove, -50, cutoffs, 0, 500);

        // Assert - should clamp to 0
        Assert.Equal(0, result);
    }

    [Fact]
    public void ValidateMovement_AboveMaxBound_ClampedToMaxBound()
    {
        // Arrange
        var gradeToMove = new Grade(LetterGrade.A, 0);
        var cutoffs = new List<GradeCutoff>
        {
            new(gradeToMove, 280),
            new(new Grade(LetterGrade.B, 1), 250),
            new(new Grade(LetterGrade.C, 2), 200)
        };

        // Act - try to move A to 600 (above max bound of 500)
        var result = _validation.ValidateMovement(gradeToMove, 600, cutoffs, 0, 500);

        // Assert - should clamp to 500
        Assert.Equal(500, result);
    }
}
