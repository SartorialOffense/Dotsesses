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

    [Fact]
    public void Calculate_WhenLowestGradeNotIncluded_TreatsItAsCatchAll()
    {
        // Arrange - This is what SHOULD happen: lowest grade (C) not passed to calculator
        // Students below the lowest visible cursor (B+) should get C
        var assessments = new List<StudentAssessment>
        {
            CreateStudent(1, 300),  // Should get A
            CreateStudent(2, 250),  // Should get B+
            CreateStudent(3, 190),  // Should get B+
            CreateStudent(4, 175),  // Should get ??? (below B+ cursor, should be C but C not in cutoffs)
            CreateStudent(5, 150),  // Should get ??? (below B+ cursor, should be C but C not in cutoffs)
        };

        var cutoffs = new List<GradeCutoff>
        {
            new(new Grade(LetterGrade.A, 0), 280),
            new(new Grade(LetterGrade.BPlus, 2), 180)  // Only visible cursors
            // C is NOT included - it's the catch-all grade
        };

        // Act
        var result = _calculator.Calculate(assessments, cutoffs);

        // Assert
        var aCount = result.First(r => r.Grade.LetterGrade == LetterGrade.A).Count;
        var bPlusCount = result.First(r => r.Grade.LetterGrade == LetterGrade.BPlus).Count;

        Assert.Equal(1, aCount);     // Student 1 (300)
        Assert.Equal(4, bPlusCount); // Students 2,3,4,5 all get B+ because there's no lower grade
    }

    [Fact]
    public void Calculate_WithLowestGradeIncluded_CreatesPhantomBoundary()
    {
        // Arrange - This demonstrates the bug where including lowest grade creates phantom boundary
        var assessments = new List<StudentAssessment>
        {
            CreateStudent(1, 300),  // Should get A
            CreateStudent(2, 190),  // Should get B+
            CreateStudent(3, 180),  // Should get B+ (at B+ cursor)
            CreateStudent(4, 170),  // Should get C (below B+ cursor)
            CreateStudent(5, 150),  // Should get C (below B+ cursor)
        };

        // With C cursor included (current buggy behavior)
        var cutoffsWithLowest = new List<GradeCutoff>
        {
            new(new Grade(LetterGrade.A, 0), 280),
            new(new Grade(LetterGrade.BPlus, 2), 180),  // Second-to-last cursor
            new(new Grade(LetterGrade.C, 4), 175)       // Lowest grade - SHOULD NOT BE HERE
        };

        // Act
        var result = _calculator.Calculate(assessments, cutoffsWithLowest);

        // Assert - Shows the phantom boundary at C's position (175)
        var cCount = result.First(r => r.Grade.LetterGrade == LetterGrade.C).Count;
        
        // Students 4 (170) and 5 (150) both have score < 175, so they fall back to C
        // But this creates a phantom boundary - counts stop updating when B+ moves below 175
        Assert.Equal(2, cCount);
    }

    [Fact]
    public void Calculate_WithoutLowestGrade_WorksCorrectly()
    {
        // Arrange - Correct behavior: exclude lowest grade from cutoffs
        var assessments = new List<StudentAssessment>
        {
            CreateStudent(1, 300),  // Should get A
            CreateStudent(2, 190),  // Should get B+
            CreateStudent(3, 180),  // Should get B+ (at B+ cursor)
            CreateStudent(4, 170),  // Should get C (below B+ cursor, fallback to lowest)
            CreateStudent(5, 150),  // Should get C (below B+ cursor, fallback to lowest)
        };

        // Without C cursor (correct behavior - C is catch-all)
        var cutoffsWithoutLowest = new List<GradeCutoff>
        {
            new(new Grade(LetterGrade.A, 0), 280),
            new(new Grade(LetterGrade.BPlus, 2), 180)  // Only visible cursors
            // C is NOT included - it gets everyone below 180
        };

        // Act
        var result = _calculator.Calculate(assessments, cutoffsWithoutLowest);

        // Assert
        var aCount = result.First(r => r.Grade.LetterGrade == LetterGrade.A).Count;
        var bPlusCount = result.First(r => r.Grade.LetterGrade == LetterGrade.BPlus).Count;
        
        Assert.Equal(1, aCount);     // Student 1 (300)
        Assert.Equal(4, bPlusCount); // Students 2,3,4,5 all get B+ because C is not in cutoffs
    }

    [Fact]
    public void Calculate_WhenCPlusSlidesDown_CountsUpdateCorrectly()
    {
        // Arrange - Simulate sliding C+ cursor down past C cursor position
        var assessments = new List<StudentAssessment>
        {
            CreateStudent(1, 300),  // A
            CreateStudent(2, 250),  // B+
            CreateStudent(3, 200),  // C+ when at 180, or C when C+ moves below 200
            CreateStudent(4, 180),  // C+ when at 180, or C when C+ moves below 180
            CreateStudent(5, 170),  // C (below all cursors)
            CreateStudent(6, 160),  // C (below all cursors)
        };

        // Scenario 1: C+ at 180 (above C at 175)
        var cutoffs1 = new List<GradeCutoff>
        {
            new(new Grade(LetterGrade.A, 0), 280),
            new(new Grade(LetterGrade.BPlus, 2), 240),
            new(new Grade(LetterGrade.CPlus, 5), 180),  // C+ cursor
            new(new Grade(LetterGrade.C, 6), 175)       // C cursor (lowest/catch-all)
        };

        var result1 = _calculator.Calculate(assessments, cutoffs1);
        var cPlusCount1 = result1.First(r => r.Grade.LetterGrade == LetterGrade.CPlus).Count;
        var cCount1 = result1.First(r => r.Grade.LetterGrade == LetterGrade.C).Count;
        
        // Students 3(200) and 4(180) get C+, students 5(170) and 6(160) get C
        Assert.Equal(2, cPlusCount1);
        Assert.Equal(2, cCount1);


    }

    private StudentAssessment CreateStudent(int id, double totalScore)
    {
        var scores = new List<Score> { new Score("Total", null, totalScore) };
        var attributes = new List<StudentAttribute>();
        return new StudentAssessment(id, scores, attributes, $"Student {id}");
    }
}
