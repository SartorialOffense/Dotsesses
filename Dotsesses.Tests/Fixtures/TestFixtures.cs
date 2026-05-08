namespace Dotsesses.Tests.Fixtures;

using System.IO;
using Dotsesses.Calculators;
using Dotsesses.Models;
using Dotsesses.UI;

/// <summary>
/// Builders for a predictable ClassAssessment + GradingSession used
/// across GradingSessionTests. The fixture is deliberately minimal —
/// add knobs only when a test needs them.
/// </summary>
public static class TestFixtures
{
    /// <summary>
    /// Absolute path to the IP exam Excel fixture used by the
    /// MainWindowViewModel test suite. Resolved by walking up from
    /// the test binary to the repo root (located by <c>Dotsesses.sln</c>).
    /// Lives under <c>Dotsesses.Tests/TestData/</c> per TD005.
    /// </summary>
    public static string IpExamScoresXlsx() => ResolveRepoFile(
        Path.Combine("Dotsesses.Tests", "TestData", "IP exam scores 2025.xlsx"));

    private static string ResolveRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Dotsesses.sln")))
        {
            dir = dir.Parent;
        }
        if (dir == null)
        {
            throw new InvalidOperationException(
                "Could not locate Dotsesses.sln walking up from " + AppContext.BaseDirectory);
        }
        return Path.Combine(dir.FullName, relativePath);
    }

    public static readonly Grade GradeA = new(LetterGrade.A, 0);
    public static readonly Grade GradeB = new(LetterGrade.B, 3);
    public static readonly Grade GradeC = new(LetterGrade.C, 6);
    public static readonly Grade GradeF = new(LetterGrade.F, 10);

    /// <summary>
    /// 50 students with AggregateGrades 50, 60, …, 540 (linear) and a
    /// tight curve A=10 / B=20 / C=20 / F=0-0. The curve filter drops
    /// F, so InitialCutoffCalculator places A=450, B=250, C=50 — making
    /// C the structural catch-all and Slots = [A, B].
    /// </summary>
    public static ClassAssessment ClassAssessmentForGrading()
    {
        var assessments = Enumerable.Range(0, 50)
            .Select(i => new StudentAssessment(
                id: i + 1,
                scores: new[] { new Score("Total", null, 50 + i * 10) },
                attributes: Array.Empty<StudentAttribute>(),
                muppetName: $"Muppet{i + 1}"))
            .ToList();

        var defaultCurve = new[]
        {
            new CutoffCountRange(GradeA, 10, 10),
            new CutoffCountRange(GradeB, 20, 20),
            new CutoffCountRange(GradeC, 20, 20),
            new CutoffCountRange(GradeF, 0, 0),
        };

        return new ClassAssessment(
            assessments: assessments,
            defaultCurve: defaultCurve,
            muppetNameMap: new Dictionary<int, MuppetNameInfo>(),
            seriesColorMap: new Dictionary<string, string>());
    }

    public static GradingSession SessionForGrading() => new(
        ClassAssessmentForGrading(),
        new CursorPlacementCalculator(),
        new CursorValidation(),
        new CutoffCountCalculator(),
        new InitialCutoffCalculator());
}
