namespace Dotsesses.Services;

using System.IO;
using Dotsesses.Models;

/// <summary>
/// Reads student assessment data from a CSV file.
/// First column is student ID, subsequent columns are named scores.
/// Blank lines are skipped.
/// </summary>
public class ScoreReader
{
    private readonly MuppetNameGenerator _nameGenerator;

    public ScoreReader()
    {
        _nameGenerator = new MuppetNameGenerator();
    }

    /// <summary>
    /// Reads student assessments from a CSV file.
    /// </summary>
    /// <param name="filePath">Path to the CSV file</param>
    /// <returns>Collection of StudentAssessments parsed from the file</returns>
    public IReadOnlyCollection<StudentAssessment> Read(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Score file not found: {filePath}");
        }

        var lines = File.ReadAllLines(filePath);
        if (lines.Length == 0)
        {
            throw new InvalidOperationException("Score file is empty");
        }

        // Parse header line to get score names
        var headerLine = lines[0].TrimStart('\uFEFF'); // Remove BOM if present
        var headers = ParseCsvLine(headerLine);

        if (headers.Count < 2)
        {
            throw new InvalidOperationException("CSV must have at least ID column and one score column");
        }

        // First column is ID, rest are score names
        var scoreNames = headers.Skip(1).ToList();

        var students = new List<StudentAssessment>();
        var studentIds = new List<int>();

        // Parse data rows
        for (int lineIndex = 1; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex].Trim();

            // Skip blank lines
            if (string.IsNullOrWhiteSpace(line) || IsBlankDataRow(line))
            {
                continue;
            }

            var values = ParseCsvLine(line);

            // Parse student ID
            if (!int.TryParse(values[0], out int studentId))
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
                var valueStr = i + 1 < values.Count ? values[i + 1] : "";

                if (double.TryParse(valueStr, out double scoreValue))
                {
                    scores.Add(new Score(scoreName, null, scoreValue));
                }
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
                new List<StudentAttribute>(), // No attributes from CSV for now
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

    private static List<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;

        foreach (char c in line)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                values.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        values.Add(current.ToString().Trim());
        return values;
    }

    private static bool IsBlankDataRow(string line)
    {
        // A row of just commas (e.g., ",,,,,,,") is considered blank
        return line.Replace(",", "").Trim().Length == 0;
    }
}
