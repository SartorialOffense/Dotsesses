namespace Dotsesses.Tests.UI;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using Dotsesses.Calculators;
using Dotsesses.Models;
using Dotsesses.Services;
using Dotsesses.UI;
using Xunit;

/// <summary>
/// Regression coverage for the end-to-end xlsx → <see cref="GradingSession"/>
/// load path. Reproduces the field report where a real gradebook whose
/// aggregate column was named "TOTAL" (uppercase) crashed on load with
/// "Cutoffs are out of order".
///
/// Root cause: <see cref="ScoreReader"/> detects the Total column
/// case-insensitively (so it does NOT synthesize one), but
/// <see cref="StudentAssessment.RecalculateAggregate"/>'s null/default
/// fallback matched the literal "Total" case-SENSITIVELY. A "TOTAL" column
/// therefore yielded AggregateGrade = 0 for every student at construction
/// time, collapsing the initial cursor layout into a non-monotonic
/// (out-of-order) state that <see cref="GradeAssigner"/> rejects.
/// </summary>
public class GradingSessionLoadRegressionTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var path in _tempFiles)
        {
            try { File.Delete(path); } catch { /* best-effort */ }
        }
    }

    private string WriteFixture(Action<IXLWorksheet> populate, string sheetName = "ALL")
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".xlsx");
        _tempFiles.Add(path);
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(sheetName);
        populate(sheet);
        workbook.SaveAs(path);
        return path;
    }

    private static GradingSession BuildSession(IReadOnlyCollection<StudentAssessment> students)
    {
        var defaultCurve = new DefaultCurveGenerator().GenerateRanges(students.Count);
        var classAssessment = new ClassAssessment(
            students.ToList(),
            defaultCurve,
            new Dictionary<int, MuppetNameInfo>(),
            new Dictionary<string, string>());

        return new GradingSession(
            classAssessment,
            new CursorPlacementCalculator(),
            new CursorValidation(),
            new CutoffCountCalculator(),
            new InitialCutoffCalculator());
    }

    [Fact]
    public void Load_UppercaseTotalColumn_DoesNotThrowOutOfOrder()
    {
        // Header "TOTAL" (uppercase) plus a component column. ScoreReader
        // detects TOTAL case-insensitively and does not synthesize a Total,
        // so the student's only Total Score is named "TOTAL".
        var path = WriteFixture(s =>
        {
            s.Cell("A1").Value = "ID";
            s.Cell("B1").Value = "Exam";
            s.Cell("C1").Value = "TOTAL";
            for (int i = 0; i < 12; i++)
            {
                int row = i + 2;
                s.Cell(row, 1).Value = 1000 + i;          // ID
                s.Cell(row, 2).Value = 40 + i;            // Exam component
                s.Cell(row, 3).Value = 120 + i * 8;       // TOTAL (varied, healthy range)
            }
        });

        var students = new ScoreReader().Read(path).Students;

        // The construction path is exactly where the field crash happened.
        var session = BuildSession(students);

        // Every student's at-construction aggregate must reflect the TOTAL
        // column, not collapse to zero.
        Assert.All(students, s => Assert.True(s.AggregateGrade > 0,
            $"Student {s.Id} aggregate was {s.AggregateGrade}; expected the TOTAL column value."));

        // And the session must hold a valid (monotonic-by-Order) cutoff set.
        var cutoffs = session.CurrentState.Cutoffs.OrderBy(c => c.Grade.Order).ToList();
        var slotCutoffs = cutoffs.Where(c => c.Grade.Order < cutoffs.Max(x => x.Grade.Order)).ToList();
        for (int i = 0; i < slotCutoffs.Count - 1; i++)
        {
            Assert.True(slotCutoffs[i].Score >= slotCutoffs[i + 1].Score,
                $"Cutoffs out of order: {slotCutoffs[i].Grade.DisplayName}={slotCutoffs[i].Score} " +
                $"< {slotCutoffs[i + 1].Grade.DisplayName}={slotCutoffs[i + 1].Score}");
        }
    }

    [Fact]
    public void Constructor_WithNarrowEqualAggregateRange_DoesNotThrowOutOfOrder()
    {
        // Independent of the casing bug: when every student shares one Total
        // (range = 0), ComputeDefaultLayout stacks the targeted slots below the
        // collapsed fallback band, which is non-monotonic by Grade.Order. The
        // constructor must rebalance instead of throwing "Cutoffs are out of order".
        var students = Enumerable.Range(0, 15)
            .Select(i => new StudentAssessment(
                id: 3000 + i,
                scores: new[] { new Score("Total", null, 200) },
                attributes: Array.Empty<StudentAttribute>(),
                muppetName: $"Muppet{i}"))
            .ToList();

        var ex = Record.Exception(() => BuildSession(students));

        Assert.Null(ex);
    }

    [Fact]
    public void Load_LowercaseTotalColumn_AggregatesFromTotal()
    {
        var path = WriteFixture(s =>
        {
            s.Cell("A1").Value = "ID";
            s.Cell("B1").Value = "Exam";
            s.Cell("C1").Value = "total";
            for (int i = 0; i < 12; i++)
            {
                int row = i + 2;
                s.Cell(row, 1).Value = 2000 + i;
                s.Cell(row, 2).Value = 30 + i;
                s.Cell(row, 3).Value = 150 + i * 5;
            }
        });

        var students = new ScoreReader().Read(path).Students;

        Assert.All(students, s => Assert.True(s.AggregateGrade > 0));
        var ex = Record.Exception(() => BuildSession(students));
        Assert.Null(ex);
    }
}
