using System.IO;
using ClosedXML.Excel;
using Dotsesses.Calculators;
using Dotsesses.Models;
using Dotsesses.UI;

namespace Dotsesses.Services;

/// <summary>
/// Service for exporting grade data to Excel files.
/// </summary>
public class ExportService
{
    /// <summary>
    /// Exports grades and distribution to two Excel files.
    /// </summary>
    /// <param name="exportDirectory">Directory to export files to</param>
    /// <param name="fileNameStem">Base filename (without extension) to prefix export files</param>
    /// <param name="students">Student assessments with grades</param>
    /// <param name="gradeAssigner">Grade assigner to determine letter grades</param>
    /// <param name="complianceRows">Compliance data for distribution report</param>
    /// <returns>Tuple of (gradesFilePath, distributionFilePath)</returns>
    public (string GradesFile, string DistributionFile) Export(
        string exportDirectory,
        string fileNameStem,
        IEnumerable<StudentAssessment> students,
        GradeAssigner gradeAssigner,
        IEnumerable<ComplianceRowViewModel> complianceRows)
    {
        var gradesFile = Path.Combine(exportDirectory, $"{fileNameStem}-Grades.xlsx");
        var distributionFile = Path.Combine(exportDirectory, $"{fileNameStem}-Grade-Distribution.xlsx");

        ExportGrades(gradesFile, students, gradeAssigner);
        ExportDistribution(distributionFile, complianceRows, students.Count());

        return (gradesFile, distributionFile);
    }

    /// <summary>
    /// Exports student grades to Excel file with columns: ID, Total Score, Letter Grade.
    /// </summary>
    private void ExportGrades(
        string filePath,
        IEnumerable<StudentAssessment> students,
        GradeAssigner gradeAssigner)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Grades");

        // Header row
        worksheet.Cell(1, 1).Value = "ID";
        worksheet.Cell(1, 2).Value = "Total Score";
        worksheet.Cell(1, 3).Value = "Letter Grade";

        // Style header
        var headerRange = worksheet.Range(1, 1, 1, 3);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;

        // Data rows
        int row = 2;
        foreach (var student in students.OrderBy(s => s.Id))
        {
            var grade = gradeAssigner.AssignGrade(student.AggregateGrade);

            worksheet.Cell(row, 1).Value = student.Id;
            worksheet.Cell(row, 2).Value = student.AggregateGrade;
            worksheet.Cell(row, 3).Value = grade.DisplayName;

            row++;
        }

        // Auto-fit columns
        worksheet.Columns().AdjustToContents();

        workbook.SaveAs(filePath);
    }

    /// <summary>
    /// Exports grade distribution to Excel file with columns: Grade, Number of Students, Percentage, Delta.
    /// </summary>
    private void ExportDistribution(
        string filePath,
        IEnumerable<ComplianceRowViewModel> complianceRows,
        int totalStudents)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Distribution");

        // Header row
        worksheet.Cell(1, 1).Value = "Grade";
        worksheet.Cell(1, 2).Value = "Number of Students";
        worksheet.Cell(1, 3).Value = "Percentage";
        worksheet.Cell(1, 4).Value = "Delta";

        // Style header
        var headerRange = worksheet.Range(1, 1, 1, 4);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;

        // Data rows - only enabled grades, ordered by grade order (A first)
        int row = 2;
        foreach (var compliance in complianceRows.Where(c => c.IsEnabled).OrderBy(c => c.Grade.Order))
        {
            var percentage = totalStudents > 0
                ? (double)compliance.CurrentCount / totalStudents
                : 0;

            var deltaText = compliance.SignedDeviation == 0
                ? ""
                : compliance.SignedDeviation > 0
                    ? $"+{compliance.SignedDeviation}"
                    : compliance.SignedDeviation.ToString();

            worksheet.Cell(row, 1).Value = compliance.Grade.DisplayName;
            worksheet.Cell(row, 2).Value = compliance.CurrentCount;
            worksheet.Cell(row, 3).Value = percentage;
            worksheet.Cell(row, 3).Style.NumberFormat.Format = "0.0%";
            worksheet.Cell(row, 4).Value = deltaText;

            // Color delta column based on value
            if (compliance.SignedDeviation < 0)
            {
                worksheet.Cell(row, 4).Style.Font.FontColor = XLColor.Red;
            }
            else if (compliance.SignedDeviation > 0)
            {
                worksheet.Cell(row, 4).Style.Font.FontColor = XLColor.Green;
            }

            row++;
        }

        // Auto-fit columns
        worksheet.Columns().AdjustToContents();

        workbook.SaveAs(filePath);
    }
}
