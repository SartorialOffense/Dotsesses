namespace Dotsesses.Tests.Calculators;

using Dotsesses.Calculators;
using Dotsesses.Models;

public class CutoffCountCalculatorTests
{
    private readonly CutoffCountCalculator _calculator = new();

    [Fact]
    public void Calculate_WithSimpleDistribution_ReturnsCorrectCounts()
    {
        // Arrange
        var assessments = new List<StudentAssessment>
        {
            CreateStudent(1, 300),
            CreateStudent(2, 285),
            CreateStudent(3, 270),
            CreateStudent(4, 250),
            CreateStudent(5, 200),
            CreateStudent(6, 150),
            CreateStudent(7, 100)
        };

        var cutoffs = new List<GradeCutoff>
        {
            new(new Grade(LetterGrade.A, 0), 280),
            new(new Grade(LetterGrade.B, 1), 240),
            new(new Grade(LetterGrade.C, 2), 180),
            new(new Grade(LetterGrade.D, 3), 120)
        };

        // Act
        var result = _calculator.Calculate(assessments, cutoffs);

        // Assert
        Assert.Equal(4, result.Count);
        Assert.Equal(2, result.First(r => r.Grade.LetterGrade == LetterGrade.A).Count);  // 300, 285
        Assert.Equal(2, result.First(r => r.Grade.LetterGrade == LetterGrade.B).Count);  // 270, 250
        Assert.Equal(1, result.First(r => r.Grade.LetterGrade == LetterGrade.C).Count);  // 200
        Assert.Equal(2, result.First(r => r.Grade.LetterGrade == LetterGrade.D).Count);  // 150, 100
    }

    [Fact]
    public void Calculate_WithTiedScores_BinsCorrectly()
    {
        // Arrange
        var assessments = new List<StudentAssessment>
        {
            CreateStudent(1, 250),
            CreateStudent(2, 250),
            CreateStudent(3, 250),
            CreateStudent(4, 200),
            CreateStudent(5, 200)
        };

        var cutoffs = new List<GradeCutoff>
        {
            new(new Grade(LetterGrade.A, 0), 250),
            new(new Grade(LetterGrade.B, 1), 200)
        };

        // Act
        var result = _calculator.Calculate(assessments, cutoffs);

        // Assert
        Assert.Equal(3, result.First(r => r.Grade.LetterGrade == LetterGrade.A).Count);
        Assert.Equal(2, result.First(r => r.Grade.LetterGrade == LetterGrade.B).Count);
    }

    [Fact]
    public void Calculate_WithSingleStudent_ReturnsCorrectCount()
    {
        // Arrange
        var assessments = new List<StudentAssessment>
        {
            CreateStudent(1, 250)
        };

        var cutoffs = new List<GradeCutoff>
        {
            new(new Grade(LetterGrade.A, 0), 200)
        };

        // Act
        var result = _calculator.Calculate(assessments, cutoffs);

        // Assert
        Assert.Single(result);
        Assert.Equal(1, result.First().Count);
    }

    [Fact]
    public void Calculate_WithEmptyGradeBin_ReturnsZeroCount()
    {
        // Arrange
        var assessments = new List<StudentAssessment>
        {
            CreateStudent(1, 300),
            CreateStudent(2, 100)
        };

        var cutoffs = new List<GradeCutoff>
        {
            new(new Grade(LetterGrade.A, 0), 280),
            new(new Grade(LetterGrade.B, 1), 200),
            new(new Grade(LetterGrade.C, 2), 80)
        };

        // Act
        var result = _calculator.Calculate(assessments, cutoffs);

        // Assert
        Assert.Equal(0, result.First(r => r.Grade.LetterGrade == LetterGrade.B).Count);
    }

    private StudentAssessment CreateStudent(int id, double totalScore)
    {
        var scores = new List<Score> { new Score("Total", null, totalScore) };
        var attributes = new List<StudentAttribute>();
        return new StudentAssessment(id, scores, attributes, $"Student {id}");
    }
}
