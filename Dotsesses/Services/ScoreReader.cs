namespace Dotsesses.Services;

using System.Data;
using System.IO;
using System.Text;
using Dotsesses.Models;
using ExcelDataReader;

/// <summary>
/// Reads student assessment data from an Excel file (.xlsx).
/// First column is student ID, subsequent columns are named scores.
/// Columns ending with "(Notes)" contain comments for the corresponding score.
/// Blank rows are skipped.
///
/// Returns a <see cref="ReadResult"/> bundling the parsed students with
/// any non-fatal warnings the reader detected (duplicate column names,
/// sparse columns, etc.). Hard errors propagate as exceptions so the
/// caller's existing load-error dialog handles them.
/// </summary>
public class ScoreReader
{
    private const string NotesSuffix = "(Notes)";

    static ScoreReader()
    {
        // Required for ExcelDataReader to work on non-Windows platforms
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// Reads student assessments from an Excel file.
    /// </summary>
    /// <param name="filePath">Path to the Excel file (.xlsx)</param>
    /// <param name="sheetName">Optional sheet name. If null, uses the first sheet.</param>
    /// <returns>Parsed students bundled with any non-fatal load warnings.</returns>
    public ReadResult Read(string filePath, string? sheetName = null)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Score file not found: {filePath}");
        }

        using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = ExcelReaderFactory.CreateReader(stream);

        // UseHeaderRow=false so we process row 0 ourselves and can detect
        // duplicate / blank headers before ExcelDataReader auto-suffixes them.
        var dataSet = reader.AsDataSet(new ExcelDataSetConfiguration
        {
            ConfigureDataTable = _ => new ExcelDataTableConfiguration
            {
                UseHeaderRow = false
            }
        });

        if (dataSet.Tables.Count == 0)
        {
            throw new InvalidOperationException("Excel file contains no sheets");
        }

        DataTable table;
        if (sheetName != null)
        {
            table = dataSet.Tables[sheetName]
                ?? throw new InvalidOperationException($"Sheet '{sheetName}' not found in Excel file");
        }
        else
        {
            table = dataSet.Tables[0];
        }

        if (table.Columns.Count < 2)
        {
            throw new InvalidOperationException("Excel sheet must have at least an ID column and one score column");
        }

        if (table.Rows.Count < 1)
        {
            throw new InvalidOperationException("Excel sheet is empty");
        }

        var warnings = new List<ReadWarning>();

        // ===== Header analysis =====
        var headerRow = table.Rows[0];
        var rawHeaders = new List<string>(table.Columns.Count);
        for (int i = 0; i < table.Columns.Count; i++)
        {
            rawHeaders.Add(headerRow[i]?.ToString()?.Trim() ?? string.Empty);
        }

        // Detect duplicate non-empty headers in the score-column range.
        // Case-insensitive because ExcelDataReader treats column names that way too.
        var headerDuplicates = rawHeaders
            .Skip(1)
            .Where(h => !string.IsNullOrEmpty(h))
            .GroupBy(h => h, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToList();
        foreach (var dup in headerDuplicates)
        {
            warnings.Add(new ReadWarning(
                ReadWarningKind.DuplicateColumnHeader,
                $"Column '{dup.Key}' appears {dup.Count()} times in the header row; only one will be readable."));
        }

        // Classify columns into score vs notes by index.
        var scoreColumns = new Dictionary<int, string>();      // col index -> score name
        var notesColumns = new Dictionary<string, int>();      // score name -> notes col index
        var scoreNameSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 1; i < table.Columns.Count; i++)
        {
            var columnName = rawHeaders[i];
            if (string.IsNullOrEmpty(columnName))
            {
                continue; // unlabeled columns are ignored, not auto-named
            }

            if (columnName.EndsWith(NotesSuffix, StringComparison.OrdinalIgnoreCase))
            {
                var scoreName = columnName[..^NotesSuffix.Length].TrimEnd();
                if (!notesColumns.ContainsKey(scoreName))
                {
                    notesColumns[scoreName] = i;
                }
            }
            else if (scoreNameSeen.Add(columnName))
            {
                scoreColumns[i] = columnName;
            }
        }

        // Orphan notes columns -- ones whose base name doesn't match any score column.
        foreach (var notesName in notesColumns.Keys)
        {
            if (!scoreColumns.Values.Any(s => string.Equals(s, notesName, StringComparison.OrdinalIgnoreCase)))
            {
                warnings.Add(new ReadWarning(
                    ReadWarningKind.OrphanNotesColumn,
                    $"Found a notes column for '{notesName}' but no matching score column -- the comments will not be read."));
            }
        }

        // ===== Row parsing =====
        var students = new List<StudentAssessment>();
        var idsSeen = new HashSet<int>();
        var duplicateIds = new SortedSet<int>();
        int skippedRows = 0;

        // Pre-compute once so we can synthesize Total inside the row loop
        // -- the StudentAssessment constructor's default aggregate set is
        // [("Total", null)], so Scores must already contain a Total entry
        // at ctor time for AggregateGrade to be correct on first read.
        var hasTotalColumn = scoreColumns.Values
            .Any(n => string.Equals(n, "Total", StringComparison.OrdinalIgnoreCase));

        // Skip row 0 (header). Iterate data rows.
        for (int r = 1; r < table.Rows.Count; r++)
        {
            var row = table.Rows[r];

            if (IsBlankRow(row))
            {
                continue;
            }

            var idValue = row[0];
            int studentId;
            if (idValue is double d)
            {
                studentId = (int)d;
            }
            else if (!int.TryParse(idValue?.ToString(), out studentId))
            {
                skippedRows++;
                continue;
            }

            if (!idsSeen.Add(studentId))
            {
                duplicateIds.Add(studentId);
            }

            var scores = new List<Score>();
            foreach (var (columnIndex, scoreName) in scoreColumns)
            {
                var cellValue = row[columnIndex];

                double scoreValue;
                if (cellValue is double dVal)
                {
                    scoreValue = dVal;
                }
                else if (double.TryParse(cellValue?.ToString(), out double parsed))
                {
                    scoreValue = parsed;
                }
                else
                {
                    continue; // missing value for this score on this row
                }

                string? comment = null;
                if (notesColumns.TryGetValue(scoreName, out var notesColumnIndex))
                {
                    var notesValue = row[notesColumnIndex];
                    if (notesValue != null && notesValue != DBNull.Value)
                    {
                        var notesText = notesValue.ToString()?.Trim();
                        if (!string.IsNullOrEmpty(notesText) && notesText != "0")
                        {
                            var lines = notesText.Split(';')
                                .Select(line => line.Trim())
                                .Where(line => !string.IsNullOrEmpty(line));
                            comment = string.Join("\n", lines);
                            if (string.IsNullOrEmpty(comment))
                            {
                                comment = null;
                            }
                        }
                    }
                }

                scores.Add(new Score(scoreName, null, scoreValue, comment));
            }

            if (!hasTotalColumn)
            {
                var total = scores.Sum(s => s.Value);
                scores.Add(new Score("Total", null, total));
            }

            students.Add(new StudentAssessment(
                studentId,
                scores,
                new List<StudentAttribute>(),
                muppetName: string.Empty));
        }

        if (students.Count == 0)
        {
            throw new InvalidOperationException(
                "No student rows could be read -- the first column contained no parseable IDs.");
        }

        if (skippedRows > 0)
        {
            warnings.Add(new ReadWarning(
                ReadWarningKind.SkippedRows,
                $"{skippedRows} row(s) with data were skipped because the first column wasn't a valid student ID."));
        }

        if (duplicateIds.Count > 0)
        {
            warnings.Add(new ReadWarning(
                ReadWarningKind.DuplicateStudentId,
                $"Duplicate student ID(s) detected: {string.Join(", ", duplicateIds)}. Downstream lookups may pick the wrong row."));
        }

        // ===== Per-column analysis (sparse / constant) =====
        foreach (var (_, scoreName) in scoreColumns.OrderBy(kvp => kvp.Key))
        {
            var values = students
                .Select(s => s.Scores.FirstOrDefault(sc =>
                    string.Equals(sc.Name, scoreName, StringComparison.Ordinal) && sc.Index == null))
                .Where(sc => sc != null)
                .Select(sc => sc!.Value)
                .ToList();

            if (values.Count < students.Count)
            {
                warnings.Add(new ReadWarning(
                    ReadWarningKind.SparseColumn,
                    $"'{scoreName}' has values for {values.Count} of {students.Count} students."));
            }

            if (values.Count > 1 && values.Distinct().Count() == 1)
            {
                warnings.Add(new ReadWarning(
                    ReadWarningKind.ConstantColumn,
                    $"'{scoreName}' has the same value ({values[0]}) for every student -- its violin will render as a flat midline."));
            }
        }

        // ===== Total synthesis warning =====
        // The actual Total Scores were appended inside the row loop above so
        // that the StudentAssessment constructor's default aggregate set
        // (which sums "Total") sees them and reports a correct initial
        // AggregateGrade. Surface the warning once here for the UI.
        if (!hasTotalColumn)
        {
            warnings.Add(new ReadWarning(
                ReadWarningKind.NoTotalColumn,
                "No 'Total' column was found, so one was synthesized as the sum of every numeric column. " +
                "If your spreadsheet already has an aggregate column (e.g. 'ALL', 'Sum', 'Final'), " +
                "rename it to 'Total' to avoid double-counting in the default aggregate."));
        }

        // ===== Muppet names =====
        var nameGenerator = new MuppetNameGenerator();
        var muppetNames = nameGenerator.Generate(students.Select(s => s.Id).OrderBy(id => id));
        var named = new List<StudentAssessment>(students.Count);
        foreach (var student in students)
        {
            var muppetName = muppetNames.TryGetValue(student.Id, out var nameInfo)
                ? nameInfo.DisplayName
                : $"Student {student.Id}";

            named.Add(new StudentAssessment(
                student.Id,
                student.Scores,
                student.Attributes,
                muppetName));
        }

        return new ReadResult(named, warnings);
    }

    private static bool IsBlankRow(DataRow row)
    {
        foreach (var item in row.ItemArray)
        {
            if (item != null && item != DBNull.Value && !string.IsNullOrWhiteSpace(item.ToString()))
            {
                return false;
            }
        }
        return true;
    }
}
