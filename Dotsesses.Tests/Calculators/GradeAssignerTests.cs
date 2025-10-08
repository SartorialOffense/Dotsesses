namespace Dotsesses.Tests.Calculators;

using Dotsesses.Calculators;
using Dotsesses.Models;

public class GradeAssignerTests
{
    private readonly GradeAssigner _assigner = new();

    [Fact]
    public void AssignGrade_WithProperlyOrderedCutoffs_ReturnsCorrectGrade()
    {
        // Arrange
        var cutoffs = new List<GradeCutoff>
        {
            new(new Grade(LetterGrade.A, 0), 280),
            new(new Grade(LetterGrade.B, 1), 250),
            new(new Grade(LetterGrade.C, 2), 200)
        };

        // Act & Assert
        Assert.Equal(LetterGrade.A, _assigner.AssignGrade(300, cutoffs).LetterGrade);
        Assert.Equal(LetterGrade.A, _assigner.AssignGrade(280, cutoffs).LetterGrade);
        Assert.Equal(LetterGrade.B, _assigner.AssignGrade(260, cutoffs).LetterGrade);
        Assert.Equal(LetterGrade.B, _assigner.AssignGrade(250, cutoffs).LetterGrade);
        Assert.Equal(LetterGrade.C, _assigner.AssignGrade(220, cutoffs).LetterGrade);
        Assert.Equal(LetterGrade.C, _assigner.AssignGrade(200, cutoffs).LetterGrade);
        Assert.Equal(LetterGrade.C, _assigner.AssignGrade(150, cutoffs).LetterGrade); // Below all -> lowest grade
    }

    [Fact]
    public void AssignGrade_WithOutOfOrderCutoffs_ThrowsException()
    {
        // Arrange - BPlus (better grade, Order 2) has LOWER score than C (worse grade, Order 4) - BUG!
        var cutoffs = new List<GradeCutoff>
        {
            new(new Grade(LetterGrade.A, 0), 280),
            new(new Grade(LetterGrade.BPlus, 2), 175),  // Better grade (lower Order), lower score - BUG!
            new(new Grade(LetterGrade.C, 4), 200)       // Worse grade (higher Order), higher score - BUG!
        };

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => _assigner.AssignGrade(190, cutoffs));
        Assert.Contains("out of order", ex.Message);
        Assert.Contains("B+", ex.Message);
        Assert.Contains("C", ex.Message);
    }

    [Fact]
    public void AssignGrade_WithSingleCutoff_Works()
    {
        // Arrange
        var cutoffs = new List<GradeCutoff>
        {
            new(new Grade(LetterGrade.C, 2), 200)
        };

        // Act & Assert
        Assert.Equal(LetterGrade.C, _assigner.AssignGrade(250, cutoffs).LetterGrade);
        Assert.Equal(LetterGrade.C, _assigner.AssignGrade(200, cutoffs).LetterGrade);
        Assert.Equal(LetterGrade.C, _assigner.AssignGrade(150, cutoffs).LetterGrade);
    }

    [Fact]
    public void AssignGrade_WithEmptyCutoffs_ThrowsException()
    {
        // Arrange
        var cutoffs = new List<GradeCutoff>();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => _assigner.AssignGrade(200, cutoffs));
        Assert.Contains("No grades available", ex.Message);
    }

    [Fact]
    public void AssignGrade_WithEqualCutoffScores_Works()
    {
        // Arrange - Two grades with same cutoff score (edge case but valid)
        var cutoffs = new List<GradeCutoff>
        {
            new(new Grade(LetterGrade.A, 0), 250),
            new(new Grade(LetterGrade.B, 1), 250),  // Same score as A (tied cutoff)
            new(new Grade(LetterGrade.C, 2), 200)
        };

        // Act & Assert - Should give better grade when tied
        Assert.Equal(LetterGrade.A, _assigner.AssignGrade(250, cutoffs).LetterGrade);
    }

    [Fact]
    public void AssignGrade_BelowAllCutoffs_ReturnsLowestGrade()
    {
        // Arrange
        var cutoffs = new List<GradeCutoff>
        {
            new(new Grade(LetterGrade.A, 0), 280),
            new(new Grade(LetterGrade.B, 1), 250),
            new(new Grade(LetterGrade.F, 9), 100)  // F is lowest grade (highest Order)
        };

        // Act
        var grade = _assigner.AssignGrade(50, cutoffs); // Below all cutoffs

        // Assert
        Assert.Equal(LetterGrade.F, grade.LetterGrade);
    }
}
