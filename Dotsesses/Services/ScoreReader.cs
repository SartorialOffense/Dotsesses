namespace Dotsesses.Services;

using System.Data;
using System.IO;
using System.Text;
using Dotsesses.Models;
using ExcelDataReader;

/// <summary>
/// Reads student assessment data from an Excel file (.xlsx).
/// First column is student ID, subsequent columns are named scores.
/// Blank rows are skipped.
/// </summary>
public class ScoreReader
{
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

        // First column is ID, rest are score names
        var scoreNames = new List<string>();
        for (int i = 1; i < table.Columns.Count; i++)
        {
            scoreNames.Add(table.Columns[i].ColumnName);
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

            // Parse scores
            var scores = new List<Score>();
            for (int i = 0; i < scoreNames.Count; i++)
            {
                var scoreName = scoreNames[i];
                var cellValue = row[i + 1];

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

                scores.Add(new Score(scoreName, null, scoreValue));
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
