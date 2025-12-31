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
/// </summary>
public class ScoreReader
{
    private const string NotesSuffix = "(Notes)";
    private readonly MuppetNameGenerator _nameGenerator;

    static ScoreReader()
    {
        // Required for ExcelDataReader to work on non-Windows platforms
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public ScoreReader()
    {
        _nameGenerator = new MuppetNameGenerator();
    }

    /// <summary>
    /// Reads student assessments from an Excel file.
    /// </summary>
    /// <param name="filePath">Path to the Excel file (.xlsx)</param>
    /// <param name="sheetName">Optional sheet name. If null, uses the first sheet.</param>
    /// <returns>Collection of StudentAssessments parsed from the file</returns>
    public IReadOnlyCollection<StudentAssessment> Read(string filePath, string? sheetName = null)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Score file not found: {filePath}");
        }

        using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = ExcelReaderFactory.CreateReader(stream);

        var dataSet = reader.AsDataSet(new ExcelDataSetConfiguration
        {
            ConfigureDataTable = _ => new ExcelDataTableConfiguration
            {
                UseHeaderRow = true
            }
        });

        if (dataSet.Tables.Count == 0)
        {
            throw new InvalidOperationException("Excel file contains no sheets");
        }

        // Get the target sheet
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
            throw new InvalidOperationException("Excel sheet must have at least ID column and one score column");
        }

        // Build column info: separate score columns from notes columns
        // Key: column index, Value: column name
        var scoreColumns = new Dictionary<int, string>();
        var notesColumns = new Dictionary<string, int>(); // Key: score name, Value: column index

        for (int i = 1; i < table.Columns.Count; i++)
        {
            var columnName = table.Columns[i].ColumnName;

            if (columnName.EndsWith(NotesSuffix, StringComparison.OrdinalIgnoreCase))
            {
                // This is a notes column - extract the score name it belongs to
                var scoreName = columnName[..^NotesSuffix.Length];
                notesColumns[scoreName] = i;
            }
            else
            {
                // This is a score column
                scoreColumns[i] = columnName;
            }
        }

        var students = new List<StudentAssessment>();
        var studentIds = new List<int>();

        // Parse data rows
        foreach (DataRow row in table.Rows)
        {
            // Skip blank rows
            if (IsBlankRow(row))
            {
                continue;
            }

            // Parse student ID from first column
            var idValue = row[0];
            int studentId;

            if (idValue is double d)
            {
                studentId = (int)d;
            }
            else if (!int.TryParse(idValue?.ToString(), out studentId))
            {
                // Skip rows without valid student ID
                continue;
            }

            studentIds.Add(studentId);

            // Parse scores with their comments
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
                    continue; // Skip non-numeric values
                }

                // Check for corresponding notes column
                string? comment = null;
                if (notesColumns.TryGetValue(scoreName, out var notesColumnIndex))
                {
                    var notesValue = row[notesColumnIndex];
                    if (notesValue != null && notesValue != DBNull.Value)
                    {
                        var notesText = notesValue.ToString()?.Trim();
                        // Treat "0" as blank (no comment)
                        if (!string.IsNullOrEmpty(notesText) && notesText != "0")
                        {
                            // Replace semicolons with newlines and trim each line
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

            // Calculate total if not present
            if (!scores.Any(s => s.Name.Equals("Total", StringComparison.OrdinalIgnoreCase)))
            {
                var total = scores.Sum(s => s.Value);
                scores.Add(new Score("Total", null, total));
            }

            students.Add(new StudentAssessment(
                studentId,
                scores,
                new List<StudentAttribute>(),
                "" // MuppetName will be assigned after all IDs are collected
            ));
        }

        // Generate MuppetNames for all students
        var muppetNames = _nameGenerator.Generate(studentIds.OrderBy(id => id));

        // Create final StudentAssessments with MuppetNames
        var result = new List<StudentAssessment>();
        foreach (var student in students)
        {
            var muppetName = muppetNames.TryGetValue(student.Id, out var nameInfo)
                ? nameInfo.DisplayName
                : $"Student {student.Id}";

            result.Add(new StudentAssessment(
                student.Id,
                student.Scores,
                student.Attributes,
                muppetName
            ));
        }

        return result;
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
