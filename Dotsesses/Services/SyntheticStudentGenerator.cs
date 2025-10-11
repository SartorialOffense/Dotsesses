namespace Dotsesses.Services;

using Dotsesses.Models;

/// <summary>
/// Generates synthetic test data for 100 students with tri-modal distribution.
/// </summary>
public class SyntheticStudentGenerator
{
    private readonly Random _random;
    private readonly MuppetNameGenerator _nameGenerator;

    public SyntheticStudentGenerator(int seed = 42)
    {
        _random = new Random(seed);
        _nameGenerator = new MuppetNameGenerator();
    }

    /// <summary>
    /// Generates 100 students with realistic score distributions and attributes.
    /// </summary>
    public IReadOnlyCollection<StudentAssessment> Generate()
    {
        var students = new List<StudentAssessment>();
        var studentIds = Enumerable.Range(1, 100).ToList();

        // Generate MuppetNames for all students
        var muppetNames = _nameGenerator.Generate(studentIds);

        // Determine performance tiers
        var tiers = AssignPerformanceTiers(100);

        for (int i = 0; i < 100; i++)
        {
            int studentId = studentIds[i];
            var tier = tiers[i];

            var scores = GenerateScores(tier);
            var attributes = GenerateAttributes(tier);
            var muppetName = muppetNames[studentId].DisplayName;

            students.Add(new StudentAssessment(studentId, scores, attributes, muppetName));
        }

        return students;
    }

    private List<PerformanceTier> AssignPerformanceTiers(int count)
    {
        var tiers = new List<PerformanceTier>();

        // 5% high performers
        int highCount = (int)(count * 0.05);
        for (int i = 0; i < highCount; i++)
        {
            tiers.Add(PerformanceTier.High);
        }

        // 75% middle performers
        int middleCount = (int)(count * 0.75);
        for (int i = 0; i < middleCount; i++)
        {
            tiers.Add(PerformanceTier.Middle);
        }

        // Remaining are low performers (~20%)
        int lowCount = count - highCount - middleCount;
        for (int i = 0; i < lowCount; i++)
        {
            tiers.Add(PerformanceTier.Low);
        }

        // Shuffle to randomize distribution
        for (int i = tiers.Count - 1; i > 0; i--)
        {
            int j = _random.Next(i + 1);
            (tiers[i], tiers[j]) = (tiers[j], tiers[i]);
        }

        return tiers;
    }

    private IReadOnlyCollection<Score> GenerateScores(PerformanceTier tier)
    {
        double quizTotal, participationTotal, final;

        switch (tier)
        {
            case PerformanceTier.High:
                // Aggregate > 250 (out of 340 total)
                quizTotal = _random.Next(18, 21);
                participationTotal = _random.Next(18, 21);
                final = _random.Next(220, 301);
                break;

            case PerformanceTier.Middle:
                // Aggregate 150-225
                quizTotal = _random.Next(10, 19);
                participationTotal = _random.Next(10, 19);
                final = _random.Next(130, 190);
                break;

            case PerformanceTier.Low:
            default:
                // Aggregate 50-125
                quizTotal = _random.Next(5, 13);
                participationTotal = _random.Next(5, 13);
                final = _random.Next(40, 100);
                break;
        }

        // Break down Quiz Total into three quizzes
        var (quiz1, quiz2, quiz3) = RandomSplit3(quizTotal);

        // Break down Final into Short Answer and MC (60/40 split approximately)
        var (finalShortAnswer, finalMC) = RandomSplit2(final, 0.6);

        // Calculate overall total
        double total = participationTotal + quizTotal + final;

        return new List<Score>
        {
            new Score("Participation Total", null, participationTotal),
            new Score("Quiz 1", null, quiz1),
            new Score("Quiz 2", null, quiz2),
            new Score("Quiz 3", null, quiz3),
            new Score("Quiz Total", null, quizTotal),
            new Score("Final MC", null, finalMC),
            new Score("Final Short Answer", null, finalShortAnswer),
            new Score("Final", null, final),
            new Score("Total", null, total)
        };
    }

    /// <summary>
    /// Randomly splits a value into 3 parts that sum to the original.
    /// </summary>
    private (double, double, double) RandomSplit3(double total)
    {
        // Generate two random split points
        double r1 = _random.NextDouble();
        double r2 = _random.NextDouble();

        // Ensure r1 < r2
        if (r1 > r2) (r1, r2) = (r2, r1);

        double part1 = Math.Round(total * r1, 2);
        double part2 = Math.Round(total * (r2 - r1), 2);
        double part3 = Math.Round(total - part1 - part2, 2);

        return (part1, part2, part3);
    }

    /// <summary>
    /// Randomly splits a value into 2 parts around a target ratio.
    /// </summary>
    private (double, double) RandomSplit2(double total, double targetRatio = 0.5)
    {
        // Add some randomness around the target ratio (±10%)
        double ratio = targetRatio + (_random.NextDouble() - 0.5) * 0.2;
        ratio = Math.Clamp(ratio, 0.3, 0.7);

        double part1 = Math.Round(total * ratio, 2);
        double part2 = Math.Round(total - part1, 2);

        return (part1, part2);
    }

    private IReadOnlyCollection<StudentAttribute> GenerateAttributes(PerformanceTier tier)
    {
        // 60% correlation with performance, 40% independent
        bool useCorrelated = _random.NextDouble() < 0.6;

        string outline, midTerm;

        if (useCorrelated)
        {
            switch (tier)
            {
                case PerformanceTier.High:
                    outline = "Yes";
                    midTerm = "✓✓+";
                    break;

                case PerformanceTier.Middle:
                    outline = _random.NextDouble() < 0.7 ? "Yes" : "No";
                    double midRoll = _random.NextDouble();
                    midTerm = midRoll < 0.7 ? "✓✓+" :
                             midRoll < 0.9 ? "✓+" : "✓";
                    break;

                case PerformanceTier.Low:
                default:
                    outline = _random.NextDouble() < 0.1 ? "Yes" : "No";
                    midTerm = _random.NextDouble() < 0.2 ? "✓" : "✓-";
                    break;
            }
        }
        else
        {
            // Independent roll
            outline = _random.NextDouble() < 0.5 ? "Yes" : "No";
            double roll = _random.NextDouble();
            midTerm = roll < 0.25 ? "✓✓+" :
                     roll < 0.50 ? "✓+" :
                     roll < 0.75 ? "✓" : "✓-";
        }

        return new List<StudentAttribute>
        {
            new StudentAttribute("Submitted Outline", null, outline),
            new StudentAttribute("Mid-Term", null, midTerm)
        };
    }

    private enum PerformanceTier
    {
        High,
        Middle,
        Low
    }
}
