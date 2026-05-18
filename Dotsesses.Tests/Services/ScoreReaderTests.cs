namespace Dotsesses.Tests.Services;

using System;
using System.Collections.Generic;
using System.IO;
using ClosedXML.Excel;
using Dotsesses.Models;
using Dotsesses.Services;
using Xunit;

/// <summary>
/// Covers the warning-detection paths added to <see cref="ScoreReader"/>:
/// duplicate headers, orphan notes, duplicate IDs, skipped rows, sparse /
/// constant columns, synthesized Total, plus hard-error paths.
/// Fixtures are built in-memory via ClosedXML and written to temp files
/// so no binary test data lives in the repo.
/// </summary>
public class ScoreReaderTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var path in _tempFiles)
        {
            try { File.Delete(path); } catch { /* best-effort */ }
        }
    }

    private string WriteFixture(Action<IXLWorksheet> populate, string sheetName = "Sheet1")
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".xlsx");
        _tempFiles.Add(path);
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(sheetName);
        populate(sheet);
        workbook.SaveAs(path);
        return path;
    }

    // ===== Happy path =====

    [Fact]
    public void Read_CleanFile_HasNoWarnings()
    {
        var path = WriteFixture(s =>
        {
            s.Cell("A1").Value = "ID";
            s.Cell("B1").Value = "Midterm";
            s.Cell("C1").Value = "Total";
            s.Cell("A2").Value = 1; s.Cell("B2").Value = 80; s.Cell("C2").Value = 80;
            s.Cell("A3").Value = 2; s.Cell("B3").Value = 70; s.Cell("C3").Value = 70;
            s.Cell("A4").Value = 3; s.Cell("B4").Value = 90; s.Cell("C4").Value = 90;
        });

        var result = new ScoreReader().Read(path);

        Assert.Equal(3, result.Students.Count);
        Assert.Empty(result.Warnings);
    }

    // ===== Header warnings =====

    [Fact]
    public void Read_DuplicateColumnHeader_SurfacesWarning()
    {
        var path = WriteFixture(s =>
        {
            s.Cell("A1").Value = "ID";
            s.Cell("B1").Value = "Midterm";
            s.Cell("C1").Value = "Midterm"; // duplicate
            s.Cell("D1").Value = "Total";
            s.Cell("A2").Value = 1; s.Cell("B2").Value = 80; s.Cell("C2").Value = 81; s.Cell("D2").Value = 80;
            s.Cell("A3").Value = 2; s.Cell("B3").Value = 70; s.Cell("C3").Value = 71; s.Cell("D3").Value = 70;
        });

        var result = new ScoreReader().Read(path);

        Assert.Contains(result.Warnings, w => w.Kind == ReadWarningKind.DuplicateColumnHeader);
    }

    [Fact]
    public void Read_OrphanNotesColumn_SurfacesWarning()
    {
        var path = WriteFixture(s =>
        {
            s.Cell("A1").Value = "ID";
            s.Cell("B1").Value = "Midterm";
            s.Cell("C1").Value = "Final (Notes)"; // no matching "Final" score column
            s.Cell("D1").Value = "Total";
            s.Cell("A2").Value = 1; s.Cell("B2").Value = 80; s.Cell("D2").Value = 80;
            s.Cell("A3").Value = 2; s.Cell("B3").Value = 70; s.Cell("D3").Value = 70;
        });

        var result = new ScoreReader().Read(path);

        Assert.Contains(result.Warnings, w => w.Kind == ReadWarningKind.OrphanNotesColumn);
    }

    // ===== Row warnings =====

    [Fact]
    public void Read_DuplicateStudentId_SurfacesWarning()
    {
        var path = WriteFixture(s =>
        {
            s.Cell("A1").Value = "ID";
            s.Cell("B1").Value = "Total";
            s.Cell("A2").Value = 1; s.Cell("B2").Value = 80;
            s.Cell("A3").Value = 1; s.Cell("B3").Value = 75; // dup
            s.Cell("A4").Value = 2; s.Cell("B4").Value = 70;
        });

        var result = new ScoreReader().Read(path);

        Assert.Contains(result.Warnings, w => w.Kind == ReadWarningKind.DuplicateStudentId);
    }

    [Fact]
    public void Read_RowsWithoutValidId_AreSkippedAndCounted()
    {
        var path = WriteFixture(s =>
        {
            s.Cell("A1").Value = "ID";
            s.Cell("B1").Value = "Total";
            s.Cell("A2").Value = 1;       s.Cell("B2").Value = 80;
            s.Cell("A3").Value = "AVG";   s.Cell("B3").Value = 75; // junk row inside data
            s.Cell("A4").Value = 2;       s.Cell("B4").Value = 70;
        });

        var result = new ScoreReader().Read(path);

        Assert.Equal(2, result.Students.Count);
        Assert.Contains(result.Warnings, w => w.Kind == ReadWarningKind.SkippedRows);
    }

    [Fact]
    public void Read_BlankRowsBetweenData_DoNotCountAsSkipped()
    {
        var path = WriteFixture(s =>
        {
            s.Cell("A1").Value = "ID";
            s.Cell("B1").Value = "Total";
            s.Cell("A2").Value = 1; s.Cell("B2").Value = 80;
            // row 3 entirely blank
            s.Cell("A4").Value = 2; s.Cell("B4").Value = 70;
        });

        var result = new ScoreReader().Read(path);

        Assert.Equal(2, result.Students.Count);
        Assert.DoesNotContain(result.Warnings, w => w.Kind == ReadWarningKind.SkippedRows);
    }

    // ===== Column distribution warnings =====

    [Fact]
    public void Read_SparseColumn_SurfacesWarning()
    {
        var path = WriteFixture(s =>
        {
            s.Cell("A1").Value = "ID";
            s.Cell("B1").Value = "Bonus";
            s.Cell("C1").Value = "Total";
            s.Cell("A2").Value = 1; s.Cell("B2").Value = 5; s.Cell("C2").Value = 80;
            s.Cell("A3").Value = 2;                          s.Cell("C3").Value = 70; // Bonus blank
            s.Cell("A4").Value = 3;                          s.Cell("C4").Value = 75; // Bonus blank
        });

        var result = new ScoreReader().Read(path);

        Assert.Contains(result.Warnings, w =>
            w.Kind == ReadWarningKind.SparseColumn && w.Message.Contains("Bonus"));
    }

    [Fact]
    public void Read_ConstantColumn_SurfacesWarning()
    {
        var path = WriteFixture(s =>
        {
            s.Cell("A1").Value = "ID";
            s.Cell("B1").Value = "Attendance";
            s.Cell("C1").Value = "Total";
            s.Cell("A2").Value = 1; s.Cell("B2").Value = 2; s.Cell("C2").Value = 80;
            s.Cell("A3").Value = 2; s.Cell("B3").Value = 2; s.Cell("C3").Value = 70;
            s.Cell("A4").Value = 3; s.Cell("B4").Value = 2; s.Cell("C4").Value = 75;
        });

        var result = new ScoreReader().Read(path);

        Assert.Contains(result.Warnings, w =>
            w.Kind == ReadWarningKind.ConstantColumn && w.Message.Contains("Attendance"));
    }

    // ===== Total synthesis =====

    [Fact]
    public void Read_NoTotalColumn_SynthesizesTotalAndWarns()
    {
        var path = WriteFixture(s =>
        {
            s.Cell("A1").Value = "ID";
            s.Cell("B1").Value = "Midterm";
            s.Cell("C1").Value = "Final";
            s.Cell("A2").Value = 1; s.Cell("B2").Value = 40; s.Cell("C2").Value = 50;
            s.Cell("A3").Value = 2; s.Cell("B3").Value = 30; s.Cell("C3").Value = 40;
        });

        var result = new ScoreReader().Read(path);

        Assert.Contains(result.Warnings, w => w.Kind == ReadWarningKind.NoTotalColumn);
        var firstTotal = result.Students[0].Scores.Single(s =>
            string.Equals(s.Name, "Total", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(90, firstTotal.Value);
    }

    [Fact]
    public void Read_WithTotalColumn_DoesNotSynthesizeOrWarn()
    {
        var path = WriteFixture(s =>
        {
            s.Cell("A1").Value = "ID";
            s.Cell("B1").Value = "Midterm";
            s.Cell("C1").Value = "Total";
            s.Cell("A2").Value = 1; s.Cell("B2").Value = 40; s.Cell("C2").Value = 90;
            s.Cell("A3").Value = 2; s.Cell("B3").Value = 30; s.Cell("C3").Value = 80;
        });

        var result = new ScoreReader().Read(path);

        Assert.DoesNotContain(result.Warnings, w => w.Kind == ReadWarningKind.NoTotalColumn);
        var totals = result.Students[0].Scores.Where(s =>
            string.Equals(s.Name, "Total", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Single(totals);
        Assert.Equal(90, totals[0].Value);
    }

    // ===== Hard errors =====

    [Fact]
    public void Read_OnlyIdColumn_Throws()
    {
        var path = WriteFixture(s =>
        {
            s.Cell("A1").Value = "ID";
            s.Cell("A2").Value = 1;
            s.Cell("A3").Value = 2;
        });

        var ex = Assert.Throws<InvalidOperationException>(() => new ScoreReader().Read(path));
        Assert.Contains("ID column", ex.Message);
    }

    [Fact]
    public void Read_NoValidIds_Throws()
    {
        var path = WriteFixture(s =>
        {
            s.Cell("A1").Value = "ID";
            s.Cell("B1").Value = "Total";
            s.Cell("A2").Value = "Alpha";  s.Cell("B2").Value = 80;
            s.Cell("A3").Value = "Beta";   s.Cell("B3").Value = 70;
        });

        var ex = Assert.Throws<InvalidOperationException>(() => new ScoreReader().Read(path));
        Assert.Contains("parseable IDs", ex.Message);
    }
}
