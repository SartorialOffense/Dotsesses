namespace Dotsesses.Tests.Calculators;

using Dotsesses.Calculators;
using Dotsesses.Models;

public class InitialCutoffCalculatorTests
{
    private readonly InitialCutoffCalculator _calculator = new();

    [Fact]
    public void Calculate_WithPerfectDistribution_MatchesTargetCounts()
    {
        // Arrange - 10 students, want 5 As and 5 Bs
        var assessments = Enumerable.Range(1, 10)
            .Select(i => CreateStudent(i, 300 - (i * 10)))
            .ToList();

        var defaultCurve = new List<CutoffCount>
        {
            new(new Grade(LetterGrade.A, 0), 5),
            new(new Grade(LetterGrade.B, 1), 5)
        };

        // Act
        var result = _calculator.Calculate(assessments, defaultCurve);

        // Assert
        Assert.Equal(2, result.Count);
        var aCutoff = result.First(r => r.Grade.LetterGrade == LetterGrade.A);
        var bCutoff = result.First(r => r.Grade.LetterGrade == LetterGrade.B);

        // A cutoff should be at the 5th student's score (250) - lowest A student
        Assert.Equal(250, aCutoff.Score);
        // B cutoff should be at the 10th student's score (200) - lowest B student
        Assert.Equal(200, bCutoff.Score);
    }

    [Fact]
    public void Calculate_WithTiesAtBoundary_IncludesAllTiedStudents()
    {
        // Arrange - 3 students tied at the A/B boundary
        var assessments = new List<StudentAssessment>
        {
            CreateStudent(1, 300),
            CreateStudent(2, 290),
            CreateStudent(3, 280),  // Target 3 As, but...
            CreateStudent(4, 280),  // These are tied at boundary
            CreateStudent(5, 280),  // Should include all tied
            CreateStudent(6, 270),
            CreateStudent(7, 260)
        };

        var defaultCurve = new List<CutoffCount>
        {
            new(new Grade(LetterGrade.A, 0), 3),  // Target 3, will get 5 due to ties
            new(new Grade(LetterGrade.B, 1), 4)
        };

        // Act
        var result = _calculator.Calculate(assessments, defaultCurve);

        // Assert
        var aCutoff = result.First(r => r.Grade.LetterGrade == LetterGrade.A);
        // Should set cutoff at 280 to include all tied students
        Assert.Equal(280, aCutoff.Score);
    }

    [Fact]
    public void Calculate_WithFewerStudentsThanTargets_HandlesGracefully()
    {
        // Arrange - only 5 students but curve targets more
        var assessments = Enumerable.Range(1, 5)
            .Select(i => CreateStudent(i, 300 - (i * 10)))
            .ToList();

        var defaultCurve = new List<CutoffCount>
        {
            new(new Grade(LetterGrade.A, 0), 3),
            new(new Grade(LetterGrade.B, 1), 5),  // More than available
            new(new Grade(LetterGrade.C, 2), 2)
        };

        // Act
        var result = _calculator.Calculate(assessments, defaultCurve);

        // Assert - should place cutoffs for all grades even if no students in some bins
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void Calculate_OrdersByStudentIdForTies_ConsistentResults()
    {
        // Arrange - students with identical scores but different IDs
        var assessments = new List<StudentAssessment>
        {
            CreateStudent(5, 250),
            CreateStudent(1, 250),
            CreateStudent(3, 250),
            CreateStudent(2, 200),
            CreateStudent(4, 200)
        };

        var defaultCurve = new List<CutoffCount>
        {
            new(new Grade(LetterGrade.A, 0), 2),
            new(new Grade(LetterGrade.B, 1), 3)
        };

        // Act
        var result = _calculator.Calculate(assessments, defaultCurve);

        // Assert - should consistently handle ties
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Calculate_WithSingleStudent_PlacesCutoffCorrectly()
    {
        // Arrange
        var assessments = new List<StudentAssessment>
        {
            CreateStudent(1, 250)
        };

        var defaultCurve = new List<CutoffCount>
        {
            new(new Grade(LetterGrade.A, 0), 1)
        };

        // Act
        var result = _calculator.Calculate(assessments, defaultCurve);

        // Assert
        Assert.Single(result);
        Assert.Equal(250, result.First().Score);
    }

    private StudentAssessment CreateStudent(int id, double totalScore)
    {
        var scores = new List<Score> { new Score("Total", null, totalScore) };
        var attributes = new List<StudentAttribute>();
        return new StudentAssessment(id, scores, attributes, $"Student {id}");
    }
}
