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

    // ===== Categorical column auto-detection =====

    [Fact]
    public void Read_AllStringColumn_ClassifiesAsCategorical()
    {
        var path = WriteFixture(s =>
        {
            s.Cell("A1").Value = "ID";
            s.Cell("B1").Value = "Submitted Outline";
            s.Cell("C1").Value = "Total";
            s.Cell("A2").Value = 1; s.Cell("B2").Value = "Yes"; s.Cell("C2").Value = 80;
            s.Cell("A3").Value = 2; s.Cell("B3").Value = "No";  s.Cell("C3").Value = 70;
            s.Cell("A4").Value = 3; s.Cell("B4").Value = "Yes"; s.Cell("C4").Value = 90;
        });

        var result = new ScoreReader().Read(path);

        Assert.All(result.Students, st => Assert.DoesNotContain(st.Scores,
            sc => string.Equals(sc.Name, "Submitted Outline", StringComparison.Ordinal)));
        Assert.All(result.Students, st => Assert.Single(st.Attributes,
            a => string.Equals(a.Name, "Submitted Outline", StringComparison.Ordinal)));
        Assert.Equal("Yes", result.Students[0].Attributes.Single().Value);
        Assert.Equal("No",  result.Students[1].Attributes.Single().Value);
    }

    [Fact]
    public void Read_ColumnWithMixedNumericAndString_ClassifiesAsCategorical()
    {
        // A single non-numeric cell ("N/A") flips the whole column to categorical.
        var path = WriteFixture(s =>
        {
            s.Cell("A1").Value = "ID";
            s.Cell("B1").Value = "Midterm";
            s.Cell("C1").Value = "Total";
            s.Cell("A2").Value = 1; s.Cell("B2").Value = 80;     s.Cell("C2").Value = 80;
            s.Cell("A3").Value = 2; s.Cell("B3").Value = "N/A";  s.Cell("C3").Value = 70;
            s.Cell("A4").Value = 3; s.Cell("B4").Value = 90;     s.Cell("C4").Value = 90;
        });

        var result = new ScoreReader().Read(path);

        Assert.All(result.Students, st => Assert.DoesNotContain(st.Scores,
            sc => string.Equals(sc.Name, "Midterm", StringComparison.Ordinal)));
        Assert.Equal("80",  result.Students[0].Attributes.Single().Value);
        Assert.Equal("N/A", result.Students[1].Attributes.Single().Value);
        Assert.Equal("90",  result.Students[2].Attributes.Single().Value);
    }

    [Fact]
    public void Read_AllNumericColumn_RemainsScore()
    {
        // Regression: pre-existing behavior for clean numeric columns.
        var path = WriteFixture(s =>
        {
            s.Cell("A1").Value = "ID";
            s.Cell("B1").Value = "Midterm";
            s.Cell("C1").Value = "Total";
            s.Cell("A2").Value = 1; s.Cell("B2").Value = 80; s.Cell("C2").Value = 80;
            s.Cell("A3").Value = 2; s.Cell("B3").Value = 70; s.Cell("C3").Value = 70;
        });

        var result = new ScoreReader().Read(path);

        Assert.All(result.Students, st => Assert.Empty(st.Attributes));
        Assert.Contains(result.Students[0].Scores, sc => sc.Name == "Midterm" && sc.Value == 80);
    }

    [Fact]
    public void Read_EmptyCellInOtherwiseNumericColumn_StaysNumeric()
    {
        // A blank cell is not a non-numeric value — it does not flip the column.
        var path = WriteFixture(s =>
        {
            s.Cell("A1").Value = "ID";
            s.Cell("B1").Value = "Bonus";
            s.Cell("C1").Value = "Total";
            s.Cell("A2").Value = 1; s.Cell("B2").Value = 5; s.Cell("C2").Value = 80;
            s.Cell("A3").Value = 2;                          s.Cell("C3").Value = 70;
            s.Cell("A4").Value = 3; s.Cell("B4").Value = 7; s.Cell("C4").Value = 90;
        });

        var result = new ScoreReader().Read(path);

        Assert.All(result.Students, st => Assert.Empty(st.Attributes));
        Assert.Contains(result.Students[0].Scores, sc => sc.Name == "Bonus" && sc.Value == 5);
    }

    [Fact]
    public void Read_CategoricalColumnNamedTotal_DoesNotPreventTotalSynthesis()
    {
        // If the column actually named "Total" is categorical, the loader still
        // synthesizes a numeric Total from the remaining numeric columns.
        var path = WriteFixture(s =>
        {
            s.Cell("A1").Value = "ID";
            s.Cell("B1").Value = "Midterm";
            s.Cell("C1").Value = "Total";
            s.Cell("A2").Value = 1; s.Cell("B2").Value = 40; s.Cell("C2").Value = "Pass";
            s.Cell("A3").Value = 2; s.Cell("B3").Value = 30; s.Cell("C3").Value = "Fail";
        });

        var result = new ScoreReader().Read(path);

        Assert.Contains(result.Warnings, w => w.Kind == ReadWarningKind.NoTotalColumn);
        // Total ends up in Scores as the synthesized sum (40 and 30 respectively).
        Assert.Equal(40, result.Students[0].Scores.Single(sc => sc.Name == "Total").Value);
        Assert.Equal(30, result.Students[1].Scores.Single(sc => sc.Name == "Total").Value);
        // And "Total" the categorical lives in Attributes.
        Assert.Equal("Pass", result.Students[0].Attributes.Single(a => a.Name == "Total").Value);
        Assert.Equal("Fail", result.Students[1].Attributes.Single(a => a.Name == "Total").Value);
    }

    [Fact]
    public void Read_CategoricalColumn_DoesNotEmitConstantWarning()
    {
        // All students share "Yes" — no ConstantColumn warning for categorical
        // (that warning exists for flat-violin numeric columns, irrelevant here).
        var path = WriteFixture(s =>
        {
            s.Cell("A1").Value = "ID";
            s.Cell("B1").Value = "Submitted Outline";
            s.Cell("C1").Value = "Total";
            s.Cell("A2").Value = 1; s.Cell("B2").Value = "Yes"; s.Cell("C2").Value = 80;
            s.Cell("A3").Value = 2; s.Cell("B3").Value = "Yes"; s.Cell("C3").Value = 70;
            s.Cell("A4").Value = 3; s.Cell("B4").Value = "Yes"; s.Cell("C4").Value = 90;
        });

        var result = new ScoreReader().Read(path);

        Assert.DoesNotContain(result.Warnings, w =>
            w.Kind == ReadWarningKind.ConstantColumn && w.Message.Contains("Submitted Outline"));
    }

    [Fact]
    public void Read_CategoricalColumn_EmitsSparseWarning_WhenMissing()
    {
        // Missing cells in a categorical column still surface as Sparse — the user
        // should know if their categorical data has holes.
        var path = WriteFixture(s =>
        {
            s.Cell("A1").Value = "ID";
            s.Cell("B1").Value = "Submitted Outline";
            s.Cell("C1").Value = "Total";
            s.Cell("A2").Value = 1; s.Cell("B2").Value = "Yes"; s.Cell("C2").Value = 80;
            s.Cell("A3").Value = 2;                              s.Cell("C3").Value = 70;
            s.Cell("A4").Value = 3; s.Cell("B4").Value = "No";  s.Cell("C4").Value = 90;
        });

        var result = new ScoreReader().Read(path);

        Assert.Contains(result.Warnings, w =>
            w.Kind == ReadWarningKind.SparseColumn && w.Message.Contains("Submitted Outline"));
    }

    [Fact]
    public void Read_CategoricalColumnDetected_EmitsWarning()
    {
        var path = WriteFixture(s =>
        {
            s.Cell("A1").Value = "ID";
            s.Cell("B1").Value = "Mid-Term";
            s.Cell("C1").Value = "Total";
            s.Cell("A2").Value = 1; s.Cell("B2").Value = "✔✔+"; s.Cell("C2").Value = 80;
            s.Cell("A3").Value = 2; s.Cell("B3").Value = "✔";   s.Cell("C3").Value = 70;
        });

        var result = new ScoreReader().Read(path);

        Assert.Contains(result.Warnings, w =>
            w.Kind == ReadWarningKind.CategoricalColumnDetected && w.Message.Contains("Mid-Term"));
    }

    [Fact]
    public void Read_TrailerRowsWithoutNumericId_DoNotFlipNumericColumns()
    {
        // Real spreadsheets carry trailer rows below the roster: summary stats
        // ("AVG") and a repeated header row whose cells hold the column names as
        // text ("Midterm"). Those rows have no numeric Id, so the row loop
        // discards them — and type detection must ignore them too. Otherwise the
        // literal "Midterm" in the repeated-header row flips the Midterm column
        // to Categorical even though every actual student's Midterm is numeric.
        var path = WriteFixture(s =>
        {
            s.Cell("A1").Value = "ID";
            s.Cell("B1").Value = "Midterm";
            s.Cell("C1").Value = "Total";
            s.Cell("A2").Value = 1; s.Cell("B2").Value = 80; s.Cell("C2").Value = 80;
            s.Cell("A3").Value = 2; s.Cell("B3").Value = 70; s.Cell("C3").Value = 70;
            // Trailer rows (no numeric Id):
            s.Cell("A5").Value = "AVG";     s.Cell("B5").Value = 75; s.Cell("C5").Value = 75;
            s.Cell("A6").Value = "Midterm"; s.Cell("B6").Value = "Midterm"; s.Cell("C6").Value = "Total";
        });

        var result = new ScoreReader().Read(path);

        // Two real students; Midterm stayed numeric (in Scores, not Attributes).
        Assert.Equal(2, result.Students.Count);
        Assert.All(result.Students, st => Assert.Empty(st.Attributes));
        Assert.Contains(result.Students[0].Scores, sc => sc.Name == "Midterm" && sc.Value == 80);
        Assert.DoesNotContain(result.Warnings, w =>
            w.Kind == ReadWarningKind.CategoricalColumnDetected);
    }

    [Fact]
    public void Read_TrailerRows_DoNotMaskGenuinelyCategoricalColumns()
    {
        // The valid-Id gate must not over-correct: a column that is non-numeric
        // in a real student row is still Categorical, trailer rows notwithstanding.
        var path = WriteFixture(s =>
        {
            s.Cell("A1").Value = "ID";
            s.Cell("B1").Value = "Submitted Outline";
            s.Cell("C1").Value = "Total";
            s.Cell("A2").Value = 1; s.Cell("B2").Value = "Yes"; s.Cell("C2").Value = 80;
            s.Cell("A3").Value = 2; s.Cell("B3").Value = "No";  s.Cell("C3").Value = 70;
            s.Cell("A5").Value = "AVG"; s.Cell("C5").Value = 75; // trailer
        });

        var result = new ScoreReader().Read(path);

        Assert.Equal(2, result.Students.Count);
        Assert.All(result.Students, st => Assert.Single(st.Attributes,
            a => a.Name == "Submitted Outline"));
        Assert.Contains(result.Warnings, w =>
            w.Kind == ReadWarningKind.CategoricalColumnDetected && w.Message.Contains("Submitted Outline"));
    }

    // ===== Sort-order suffix (ADR-0017) =====

    [Fact]
    public void Read_CategoricalValuesWithSuffix_StripLabelAndDecodeSortOrder()
    {
        var path = WriteFixture(s =>
        {
            s.Cell("A1").Value = "ID";
            s.Cell("B1").Value = "Mid-Term";
            s.Cell("C1").Value = "Total";
            s.Cell("A2").Value = 1; s.Cell("B2").Value = "✔✔+~3"; s.Cell("C2").Value = 80;
            s.Cell("A3").Value = 2; s.Cell("B3").Value = "✔~1";   s.Cell("C3").Value = 70;
        });

        var result = new ScoreReader().Read(path);

        var a1 = result.Students[0].Attributes.Single(a => a.Name == "Mid-Term");
        var a2 = result.Students[1].Attributes.Single(a => a.Name == "Mid-Term");
        Assert.Equal("✔✔+", a1.Value);
        Assert.Equal(3, a1.SortOrder);
        Assert.Equal("✔", a2.Value);
        Assert.Equal(1, a2.SortOrder);
    }

    [Fact]
    public void Read_CategoricalValuesWithoutSuffix_HaveNullSortOrder()
    {
        var path = WriteFixture(s =>
        {
            s.Cell("A1").Value = "ID";
            s.Cell("B1").Value = "Submitted Outline";
            s.Cell("C1").Value = "Total";
            s.Cell("A2").Value = 1; s.Cell("B2").Value = "Yes"; s.Cell("C2").Value = 80;
            s.Cell("A3").Value = 2; s.Cell("B3").Value = "No";  s.Cell("C3").Value = 70;
        });

        var result = new ScoreReader().Read(path);

        Assert.All(result.Students, st =>
            Assert.Null(st.Attributes.Single(a => a.Name == "Submitted Outline").SortOrder));
        Assert.DoesNotContain(result.Warnings, w => w.Kind == ReadWarningKind.MixedSortOrderColumn);
    }

    [Fact]
    public void Read_MixedSuffixColumn_EmitsMixedWarning()
    {
        // Some values suffixed, some not → still Categorical, with a warning.
        var path = WriteFixture(s =>
        {
            s.Cell("A1").Value = "ID";
            s.Cell("B1").Value = "Mid-Term";
            s.Cell("C1").Value = "Total";
            s.Cell("A2").Value = 1; s.Cell("B2").Value = "Fail~1";     s.Cell("C2").Value = 80;
            s.Cell("A3").Value = 2; s.Cell("B3").Value = "Pass~2";     s.Cell("C3").Value = 70;
            s.Cell("A4").Value = 3; s.Cell("B4").Value = "Incomplete"; s.Cell("C4").Value = 60;
        });

        var result = new ScoreReader().Read(path);

        Assert.Contains(result.Warnings, w =>
            w.Kind == ReadWarningKind.MixedSortOrderColumn && w.Message.Contains("Mid-Term"));
        var incomplete = result.Students[2].Attributes.Single(a => a.Name == "Mid-Term");
        Assert.Equal("Incomplete", incomplete.Value);
        Assert.Null(incomplete.SortOrder);
    }

    [Fact]
    public void Read_ConflictingSortOrdersForSameLabel_ResolvesToMinAndWarns()
    {
        // "Pass" appears with both ~2 and ~5 → resolve to the minimum (2), warn.
        var path = WriteFixture(s =>
        {
            s.Cell("A1").Value = "ID";
            s.Cell("B1").Value = "Mid-Term";
            s.Cell("C1").Value = "Total";
            s.Cell("A2").Value = 1; s.Cell("B2").Value = "Pass~2"; s.Cell("C2").Value = 80;
            s.Cell("A3").Value = 2; s.Cell("B3").Value = "Pass~5"; s.Cell("C3").Value = 70;
            s.Cell("A4").Value = 3; s.Cell("B4").Value = "Fail~1"; s.Cell("C4").Value = 60;
        });

        var result = new ScoreReader().Read(path);

        Assert.Contains(result.Warnings, w =>
            w.Kind == ReadWarningKind.OrdinalSortOrderConflict && w.Message.Contains("Pass"));
        // Both "Pass" attributes normalized to the minimum order.
        var passOrders = result.Students
            .SelectMany(st => st.Attributes)
            .Where(a => a.Name == "Mid-Term" && a.Value == "Pass")
            .Select(a => a.SortOrder);
        Assert.All(passOrders, o => Assert.Equal(2, o));
    }

    [Fact]
    public void Read_FullySuffixedColumn_EmitsNoMixedOrConflictWarning()
    {
        var path = WriteFixture(s =>
        {
            s.Cell("A1").Value = "ID";
            s.Cell("B1").Value = "Mid-Term";
            s.Cell("C1").Value = "Total";
            s.Cell("A2").Value = 1; s.Cell("B2").Value = "Low~1";  s.Cell("C2").Value = 80;
            s.Cell("A3").Value = 2; s.Cell("B3").Value = "High~3"; s.Cell("C3").Value = 70;
            s.Cell("A4").Value = 3; s.Cell("B4").Value = "Mid~2";  s.Cell("C4").Value = 60;
        });

        var result = new ScoreReader().Read(path);

        Assert.DoesNotContain(result.Warnings, w => w.Kind == ReadWarningKind.MixedSortOrderColumn);
        Assert.DoesNotContain(result.Warnings, w => w.Kind == ReadWarningKind.OrdinalSortOrderConflict);
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
