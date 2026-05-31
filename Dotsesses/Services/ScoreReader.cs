namespace Dotsesses.Services;

using System.Data;
using System.Globalization;
using System.IO;
using System.Text;
using Dotsesses.Calculators;
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

        // ===== Type-inference pre-scan =====
        // A column is Categorical if any non-empty cell IN A STUDENT DATA ROW
        // fails to parse as a double. The row-eligibility decision (blank rows
        // AND rows without a numeric Id) MUST match the row loop below so that
        // detection and population stay consistent — otherwise non-numeric text
        // in a trailer row (e.g. a repeated header row's "Q1") flips a column to
        // Categorical even though the row loop discards that row.
        var categoricalColumns = new HashSet<int>();
        for (int r = 1; r < table.Rows.Count; r++)
        {
            var row = table.Rows[r];
            if (IsBlankRow(row)) continue;
            if (!TryReadStudentId(row[0], out _)) continue;
            foreach (var (columnIndex, _) in scoreColumns)
            {
                if (categoricalColumns.Contains(columnIndex)) continue;
                var cell = row[columnIndex];
                if (cell == null || cell == DBNull.Value) continue;
                if (cell is double) continue;
                var text = cell.ToString();
                if (string.IsNullOrWhiteSpace(text)) continue;
                if (double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands,
                                    CultureInfo.InvariantCulture, out _)) continue;
                categoricalColumns.Add(columnIndex);
            }
        }

        // One CategoricalColumnDetected warning per categorical column so the user
        // sees at-a-glance which columns flipped type at load time.
        foreach (var (columnIndex, scoreName) in scoreColumns.OrderBy(kvp => kvp.Key))
        {
            if (!categoricalColumns.Contains(columnIndex)) continue;
            warnings.Add(new ReadWarning(
                ReadWarningKind.CategoricalColumnDetected,
                $"'{scoreName}' contained non-numeric values; loaded as a categorical Student Attribute. " +
                "It will appear in the drill-down panel but not in the violin plot or correlation matrix."));
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
        // A categorical "Total" column does not satisfy this contract (the
        // values live in Attributes, not Scores) so it must still trigger
        // numeric Total synthesis from the remaining numeric columns.
        var hasTotalColumn = scoreColumns
            .Where(kvp => !categoricalColumns.Contains(kvp.Key))
            .Any(kvp => string.Equals(kvp.Value, "Total", StringComparison.OrdinalIgnoreCase));

        // Skip row 0 (header). Iterate data rows.
        for (int r = 1; r < table.Rows.Count; r++)
        {
            var row = table.Rows[r];

            if (IsBlankRow(row))
            {
                continue;
            }

            if (!TryReadStudentId(row[0], out int studentId))
            {
                skippedRows++;
                continue;
            }

            if (!idsSeen.Add(studentId))
            {
                duplicateIds.Add(studentId);
            }

            var scores = new List<Score>();
            var attributes = new List<StudentAttribute>();
            foreach (var (columnIndex, scoreName) in scoreColumns)
            {
                var cellValue = row[columnIndex];

                if (categoricalColumns.Contains(columnIndex))
                {
                    // Categorical branch: keep the raw string. Blank cells become missing
                    // (mirrors the numeric branch's "skip on unparseable" semantics).
                    if (cellValue == null || cellValue == DBNull.Value) continue;
                    var text = cellValue.ToString()?.Trim();
                    if (string.IsNullOrEmpty(text)) continue;
                    // Decode the optional ~N sort-order suffix (ADR-0017): the label is
                    // stored stripped, the rank as SortOrder. Cross-column conflict
                    // resolution / mixed-column detection happens in a post-pass below.
                    var (label, sortOrder) = SortOrderSuffixParser.Parse(text);
                    attributes.Add(new StudentAttribute(scoreName, null, label, SortOrder: sortOrder));
                    continue;
                }

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
                attributes,
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
        // Categorical columns retain the SparseColumn warning (missing values are still
        // suspicious) but skip ConstantColumn — "all Yes" is a meaningful categorical
        // pattern, not the degenerate flat-violin case the warning was designed to
        // surface.
        foreach (var (columnIndex, scoreName) in scoreColumns.OrderBy(kvp => kvp.Key))
        {
            if (categoricalColumns.Contains(columnIndex))
            {
                var presentCount = students.Count(s => s.Attributes.Any(a =>
                    string.Equals(a.Name, scoreName, StringComparison.Ordinal) && a.Index == null));
                if (presentCount < students.Count)
                {
                    warnings.Add(new ReadWarning(
                        ReadWarningKind.SparseColumn,
                        $"'{scoreName}' has values for {presentCount} of {students.Count} students."));
                }
                continue;
            }

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

        // ===== Sort-order suffix analysis (ADR-0017) =====
        // Per categorical column, surface mixed-suffix columns and resolve
        // same-label / different-N conflicts to the minimum N (normalizing every
        // student's SortOrder so downstream ordering and Ordinal detection are
        // consistent). The whole-column "every cell suffixed" Ordinal decision is
        // made later from the normalized SortOrders (see ScoreSelectionDefaults).
        AnalyzeSortOrders(students, scoreColumns, categoricalColumns, warnings);

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

    /// <summary>
    /// Post-pass over the parsed categorical columns (ADR-0017). For each column
    /// that carries any <c>~N</c> sort-order suffix:
    /// <list type="bullet">
    /// <item>If some present cells are suffixed and some are not, emit a
    /// <see cref="ReadWarningKind.MixedSortOrderColumn"/> warning (the column
    /// stays Categorical — only a fully-suffixed column becomes Ordinal).</item>
    /// <item>If the same label carries different N values, resolve to the minimum
    /// N, emit an <see cref="ReadWarningKind.OrdinalSortOrderConflict"/> warning,
    /// and normalize every student's SortOrder for that label to the minimum.</item>
    /// </list>
    /// </summary>
    private static void AnalyzeSortOrders(
        List<StudentAssessment> students,
        Dictionary<int, string> scoreColumns,
        HashSet<int> categoricalColumns,
        List<ReadWarning> warnings)
    {
        foreach (var (columnIndex, scoreName) in scoreColumns.OrderBy(kvp => kvp.Key))
        {
            if (!categoricalColumns.Contains(columnIndex)) continue;

            var columnAttrs = students
                .SelectMany(s => s.Attributes.Where(a =>
                    string.Equals(a.Name, scoreName, StringComparison.Ordinal) && a.Index == null))
                .ToList();
            if (columnAttrs.Count == 0) continue;

            var suffixed = columnAttrs.Where(a => a.SortOrder.HasValue).ToList();
            if (suffixed.Count == 0) continue; // plain categorical — nothing to analyze

            if (suffixed.Count < columnAttrs.Count)
            {
                warnings.Add(new ReadWarning(
                    ReadWarningKind.MixedSortOrderColumn,
                    $"'{scoreName}' has sort-order suffixes (~N) on some values but not all; " +
                    "unsuffixed values will sort after the suffixed ones."));
            }

            // Resolve same-label / different-N conflicts to the minimum N.
            var resolved = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var group in suffixed.GroupBy(a => a.Value, StringComparer.Ordinal))
            {
                var orders = group.Select(a => a.SortOrder!.Value).Distinct().OrderBy(n => n).ToList();
                resolved[group.Key] = orders[0];
                if (orders.Count > 1)
                {
                    warnings.Add(new ReadWarning(
                        ReadWarningKind.OrdinalSortOrderConflict,
                        $"'{scoreName}' value '{group.Key}' has conflicting sort orders " +
                        $"({string.Join(", ", orders)}); using {orders[0]}."));
                }
            }

            // Normalize every student's attribute for this column to the resolved min N.
            foreach (var student in students)
            {
                for (int i = 0; i < student.Attributes.Count; i++)
                {
                    var a = student.Attributes[i];
                    if (a.Index != null ||
                        !string.Equals(a.Name, scoreName, StringComparison.Ordinal)) continue;
                    if (a.SortOrder.HasValue &&
                        resolved.TryGetValue(a.Value, out var minN) &&
                        a.SortOrder.Value != minN)
                    {
                        student.Attributes[i] = a with { SortOrder = minN };
                    }
                }
            }
        }
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

    /// <summary>
    /// A row is a Student data row only if its first cell holds a valid integer
    /// Id. Shared by the type-inference pre-scan and the row-parsing loop so the
    /// two agree on which rows count: trailer rows that real spreadsheets carry
    /// below the roster — summary statistics ("AVG", "Stdev"), repeated header
    /// rows, max-points rows — all lack a numeric Id and must be ignored by
    /// BOTH. (Previously only the parser skipped them, so non-numeric text in a
    /// repeated header row like "Q1" wrongly flipped the Q1 column to
    /// Categorical.)
    /// </summary>
    private static bool TryReadStudentId(object? idValue, out int studentId)
    {
        if (idValue is double d)
        {
            studentId = (int)d;
            return true;
        }
        return int.TryParse(idValue?.ToString(), out studentId);
    }
}
