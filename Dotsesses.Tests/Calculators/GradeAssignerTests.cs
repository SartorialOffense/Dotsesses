namespace Dotsesses.Tests.Calculators;

using Dotsesses.Calculators;
using Dotsesses.Models;

public class GradeAssignerTests
{

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
        var assigner = new GradeAssigner(cutoffs);

        // Act & Assert
        Assert.Equal(LetterGrade.A, assigner.AssignGrade(300).LetterGrade);
        Assert.Equal(LetterGrade.A, assigner.AssignGrade(280).LetterGrade);
        Assert.Equal(LetterGrade.B, assigner.AssignGrade(260).LetterGrade);
        Assert.Equal(LetterGrade.B, assigner.AssignGrade(250).LetterGrade);
        Assert.Equal(LetterGrade.C, assigner.AssignGrade(220).LetterGrade);
        Assert.Equal(LetterGrade.C, assigner.AssignGrade(200).LetterGrade);
        Assert.Equal(LetterGrade.C, assigner.AssignGrade(150).LetterGrade); // Below all -> lowest grade
    }

    [Fact]
    public void AssignGrade_WithOutOfOrderCutoffs_ThrowsException()
    {
        // Arrange - BPlus (better grade, Order 2) has LOWER score than B (worse grade, Order 3) - BUG!
        // Note: C is the lowest grade, so it's exempt from validation (can have any score)
        var cutoffs = new List<GradeCutoff>
        {
            new(new Grade(LetterGrade.A, 0), 280),
            new(new Grade(LetterGrade.BPlus, 2), 175),  // Better grade (lower Order), lower score - BUG!
            new(new Grade(LetterGrade.B, 3), 200),      // Worse grade (higher Order), higher score - BUG!
            new(new Grade(LetterGrade.C, 4), 100)       // Lowest grade, exempt from validation
        };

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => new GradeAssigner(cutoffs));
        Assert.Contains("out of order", ex.Message);
        Assert.Contains("B+", ex.Message);
        Assert.Contains("B", ex.Message);
    }

    [Fact]
    public void AssignGrade_WithSingleCutoff_Works()
    {
        // Arrange
        var cutoffs = new List<GradeCutoff>
        {
            new(new Grade(LetterGrade.C, 2), 200)
        };
        var assigner = new GradeAssigner(cutoffs);

        // Act & Assert
        Assert.Equal(LetterGrade.C, assigner.AssignGrade(250).LetterGrade);
        Assert.Equal(LetterGrade.C, assigner.AssignGrade(200).LetterGrade);
        Assert.Equal(LetterGrade.C, assigner.AssignGrade(150).LetterGrade);
    }

    [Fact]
    public void AssignGrade_WithEmptyCutoffs_ThrowsException()
    {
        // Arrange
        var cutoffs = new List<GradeCutoff>();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => new GradeAssigner(cutoffs));
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
        var assigner = new GradeAssigner(cutoffs);

        // Act & Assert - Should give better grade when tied
        Assert.Equal(LetterGrade.A, assigner.AssignGrade(250).LetterGrade);
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
        var assigner = new GradeAssigner(cutoffs);

        // Act
        var grade = assigner.AssignGrade(50); // Below all cutoffs

        // Assert
        Assert.Equal(LetterGrade.F, grade.LetterGrade);
    }

    [Fact]
    public void AssignGrade_SecondToLastCursorBelowLowestScore_DoesNotThrow()
    {
        // Arrange - Simulates the boundary exception scenario where C+ (second-to-last) 
        // is dragged below C (lowest grade) score without validation error
        var cutoffs = new List<GradeCutoff>
        {
            new(new Grade(LetterGrade.A, 0), 280),
            new(new Grade(LetterGrade.BPlus, 2), 240),
            new(new Grade(LetterGrade.CPlus, 5), 150),  // Second-to-last cursor dragged low
            new(new Grade(LetterGrade.C, 6), 175)       // Lowest grade (catch-all) has higher score
        };
        var assigner = new GradeAssigner(cutoffs);

        // Act & Assert - Should NOT throw even though C+ (150) < C (175)
        // because C is the catch-all lowest grade and exempt from validation
        var grade1 = assigner.AssignGrade(250); // Should get BPlus
        var grade2 = assigner.AssignGrade(160); // Should get CPlus
        var grade3 = assigner.AssignGrade(140); // Should get C (below all cursors)

        Assert.Equal(LetterGrade.BPlus, grade1.LetterGrade);
        Assert.Equal(LetterGrade.CPlus, grade2.LetterGrade);
        Assert.Equal(LetterGrade.C, grade3.LetterGrade);
    }
}
