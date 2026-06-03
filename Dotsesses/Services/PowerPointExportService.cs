using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Dotsesses.Models;
using Dotsesses.UI;
using ShapeCrawler;

namespace Dotsesses.Services;

/// <summary>
/// Service for exporting presentation slides with plots and grade data.
/// </summary>
public class PowerPointExportService
{
    // Slide dimensions (16:9 standard) in points
    private const int SlideWidth = 960;
    private const int SlideHeight = 540;

    // Layout constants (points)
    private const int MarginX = 50;
    private const int TitleY = 15;
    private const int TitleHeight = 30;
    private const int ContentY = 50;
    private const int ContentWidth = 860;  // SlideWidth - 2*MarginX
    private const int MaxContentHeight = 460; // SlideHeight - ContentY - margin
    private const int FooterHeight = 46; // bottom caption band (points) — fits a 2-line legend+test note

    /// <summary>
    /// Exports a complete presentation with DotPlot, ViolinPlot, CorrelationPlot,
    /// SignificanceMatrix, and grade table.
    /// </summary>
    public async Task ExportAsync(
        string outputPath,
        Control dotPlotControl,
        Control violinPlotControl,
        Control correlationPlotControl,
        Control significancePlotControl,
        PlotTabContainerViewModel tabViewModel,
        IEnumerable<ComplianceRowViewModel> complianceRows,
        IMessenger messenger,
        string className = "Grade Analysis")
    {
        // Create presentation with first slide
        var pres = new Presentation(p => p.Slide());

        try
        {
            // Slide 1: DotPlot (don't restore theme yet - we'll batch renders)
            ClearPlaceholders(pres, 1);
            await AddPlotSlideAsync(pres, 1, $"{className} - Score Distribution", dotPlotControl, messenger, restoreTheme: false);

            // Slide 2: ViolinPlot - ensure it's visible first
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                tabViewModel.SelectViolinCommand.Execute(null);
            });
            await Task.Delay(300); // Allow layout to update

            pres.Slides.Add(1);
            ClearPlaceholders(pres, 2);
            await AddPlotSlideAsync(pres, 2, $"{className} - Component Analysis", violinPlotControl, messenger, restoreTheme: false);

            // Slide 3: CorrelationPlot - switch to correlation tab first
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                tabViewModel.SelectCorrelationCommand.Execute(null);
            });
            // Allow more time for layout + Python plot regeneration in light theme
            await Task.Delay(500);

            pres.Slides.Add(1);
            ClearPlaceholders(pres, 3);
            await AddPlotSlideAsync(pres, 3, $"{className} - Associations", correlationPlotControl, messenger, restoreTheme: false);

            // Slide 4: SignificanceMatrix - switch to significance tab first
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                tabViewModel.SelectSignificanceCommand.Execute(null);
            });
            // Allow layout + Python plot regeneration in light theme
            await Task.Delay(500);

            // Capture the active test family so the slide footer documents
            // exactly which metric produced the cell p-values and stars.
            var testFamily = await Dispatcher.UIThread.InvokeAsync(() =>
                (significancePlotControl.DataContext as SignificancePlotViewModel)?.TestFamily
                    ?? SignificanceTestFamily.Parametric);

            pres.Slides.Add(1);
            ClearPlaceholders(pres, 4);
            await AddPlotSlideAsync(pres, 4, $"{className} - Group Differences", significancePlotControl, messenger, restoreTheme: false,
                footer: BuildSignificanceFooter(testFamily));

            // Slide 5: Grade Table (no rendering needed)
            pres.Slides.Add(1);
            ClearPlaceholders(pres, 5);
            AddGradeTableSlide(pres, 5, $"{className} - Grade Breakdown", complianceRows);

            // Delete existing file if it exists (ShapeCrawler doesn't overwrite cleanly)
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            // Save presentation
            pres.Save(outputPath);
        }
        finally
        {
            // Restore to violin tab and DarkMode theme
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                tabViewModel.SelectViolinCommand.Execute(null);
            });
            ImageCopyService.RestoreDarkMode(messenger);
        }
    }

    /// <summary>
    /// Removes all placeholder shapes from a slide (like "Click to edit Master title style").
    /// </summary>
    private void ClearPlaceholders(Presentation pres, int slideNumber)
    {
        var slide = pres.Slide(slideNumber);
        var shapesToRemove = slide.Shapes.Where(s => s.PlaceholderType != null).ToList();
        foreach (var shape in shapesToRemove)
        {
            shape.Remove();
        }
    }

    /// <summary>
    /// Adds a slide with a title and centered, full-width plot image (aspect ratio preserved).
    /// </summary>
    private async Task AddPlotSlideAsync(
        Presentation pres,
        int slideNumber,
        string title,
        Control plotControl,
        IMessenger messenger,
        bool restoreTheme = true,
        string? footer = null)
    {
        var shapes = pres.Slide(slideNumber).Shapes;

        // Add title at top (centered)
        AddTitle(pres, slideNumber, title);

        // Render plot to PNG stream
        using var imageStream = await ImageCopyService.RenderControlToPngStreamAsync(plotControl, messenger, restoreTheme);

        // Get image dimensions to calculate aspect ratio
        imageStream.Position = 0;
        var bitmap = new Bitmap(imageStream);
        var imageWidth = bitmap.PixelSize.Width;
        var imageHeight = bitmap.PixelSize.Height;
        var aspectRatio = (double)imageHeight / imageWidth;

        // Reset stream position for ShapeCrawler
        imageStream.Position = 0;

        // Reserve vertical space at the bottom for a footer when present, so
        // the plot image is centered above it rather than overlapping it.
        var footerReserve = footer != null ? FooterHeight : 0;
        var maxContentHeight = MaxContentHeight - footerReserve;

        // Calculate dimensions: full width, height scaled to maintain aspect ratio
        var targetWidth = ContentWidth;
        var targetHeight = (int)(targetWidth * aspectRatio);

        // If height exceeds max, scale down from height instead
        if (targetHeight > maxContentHeight)
        {
            targetHeight = maxContentHeight;
            targetWidth = (int)(targetHeight / aspectRatio);
        }

        // Calculate centered position (horizontally centered, vertically centered in content area)
        var x = (SlideWidth - targetWidth) / 2;
        var contentAreaHeight = SlideHeight - ContentY - footerReserve;
        var y = ContentY + (contentAreaHeight - targetHeight) / 2;

        // Add plot image
        shapes.AddPicture(imageStream);
        var picture = shapes.Last();

        // Position and size (centered, aspect ratio preserved)
        picture.X = x;
        picture.Y = y;
        picture.Width = targetWidth;
        picture.Height = targetHeight;

        if (footer != null)
        {
            AddFooter(pres, slideNumber, footer);
        }
    }

    /// <summary>
    /// Adds a small, full-width caption text box pinned to the bottom of the
    /// slide — used to document the metric behind a plot (e.g. the
    /// Significance Matrix's per-cell test and star legend).
    /// </summary>
    private void AddFooter(Presentation pres, int slideNumber, string footer)
    {
        // The footer may be multi-line: each "\n"-separated segment becomes its
        // own paragraph (one visual line), rather than relying on word-wrap.
        var lines = footer.Split('\n');
        var shapes = pres.Slide(slideNumber).Shapes;
        shapes.AddShape(
            x: MarginX,
            y: SlideHeight - FooterHeight,
            width: ContentWidth,
            height: FooterHeight - 4,
            Geometry.Rectangle,
            lines[0]);

        var footerShape = shapes.Last();
        footerShape.Fill?.SetNoFill();
        footerShape.Outline?.SetNoOutline();
        footerShape.SetFontColor("555555");

        var textBox = footerShape.TextBox;
        if (textBox != null)
        {
            for (int li = 1; li < lines.Length; li++)
            {
                textBox.Paragraphs.Add();
                textBox.Paragraphs.Last().Text = lines[li];
            }
            foreach (var paragraph in textBox.Paragraphs)
            {
                paragraph.SetFontSize(9);
            }
        }
    }

    /// <summary>
    /// Builds the Significance Matrix slide footer as two lines (joined by a
    /// newline, rendered as two paragraphs by <see cref="AddFooter"/>): a stats
    /// line (test, effect size, stars, raw-p caveat) and a symbols line (IQR box,
    /// median line, individual-student dots — ADR-0019). See ADR-0015 / ADR-0018
    /// / ADR-0019.
    /// </summary>
    private static string BuildSignificanceFooter(SignificanceTestFamily testFamily)
    {
        var testName = testFamily == SignificanceTestFamily.Nonparametric
            ? "Kruskal–Wallis (non-parametric)"
            : "Welch's ANOVA (parametric)";
        var effectSymbol = testFamily == SignificanceTestFamily.Nonparametric ? "ε²" : "η²";

        var statsLine =
            $"Per-cell test: {testName}.  Effect size {effectSymbol} = variance explained (0–1).  " +
            "Stars: * p<.05  ** p<.01  *** p<.001.  Raw, uncorrected p (exploratory).";
        var symbolsLine =
            "Box = IQR (middle 50%), centre line = median, dots = individual students.";

        return statsLine + "\n" + symbolsLine;
    }

    /// <summary>
    /// Adds a slide with a title and centered grade breakdown table.
    /// </summary>
    private void AddGradeTableSlide(
        Presentation pres,
        int slideNumber,
        string title,
        IEnumerable<ComplianceRowViewModel> complianceRows)
    {
        var shapes = pres.Slide(slideNumber).Shapes;

        // Add title at top (centered)
        AddTitle(pres, slideNumber, title);

        // Filter to enabled grades, sorted by order (A first)
        var rows = complianceRows
            .Where(r => r.IsEnabled)
            .OrderBy(r => r.Grade.Order)
            .ToList();

        if (rows.Count == 0)
        {
            // Add a "No data" message if no grades (centered)
            shapes.AddShape(
                x: (SlideWidth - 300) / 2,
                y: (SlideHeight - 40) / 2,
                width: 300,
                height: 40,
                Geometry.Rectangle,
                "No grade data available");
            return;
        }

        // Calculate total for percentage
        var totalStudents = rows.Sum(r => r.CurrentCount);

        // Create table: 5 columns, (header + data rows + total row)
        // Columns: Grade | Count | Target | Percentage | Delta
        var columnCount = 5;
        var rowCount = rows.Count + 2; // +1 header, +1 total

        // Create table first at temporary position, then center it
        shapes.AddTable(
            x: 0,
            y: 0,
            columnsCount: columnCount,
            rowsCount: rowCount);

        var tableShape = shapes.Last();
        var table = tableShape.Table;

        // Header row
        table[0, 0].TextBox.SetText("Grade");
        table[0, 1].TextBox.SetText("Count");
        table[0, 2].TextBox.SetText("Target");
        table[0, 3].TextBox.SetText("Percentage");
        table[0, 4].TextBox.SetText("Delta");

        // Data rows
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var tableRow = i + 1;

            table[tableRow, 0].TextBox.SetText(row.Grade.DisplayName);
            table[tableRow, 1].TextBox.SetText(row.CurrentCount.ToString());
            table[tableRow, 2].TextBox.SetText($"{row.LowerTarget}-{row.UpperTarget}");

            var percentage = totalStudents > 0
                ? (double)row.CurrentCount / totalStudents * 100
                : 0;
            table[tableRow, 3].TextBox.SetText($"{percentage:F1}%");

            var deltaText = row.SignedDeviation == 0
                ? "-"
                : row.SignedDeviation > 0
                    ? $"+{row.SignedDeviation}"
                    : row.SignedDeviation.ToString();
            table[tableRow, 4].TextBox.SetText(deltaText);
        }

        // Total row (last row)
        var totalRow = rows.Count + 1;
        table[totalRow, 0].TextBox.SetText("Total");
        table[totalRow, 1].TextBox.SetText(totalStudents.ToString());
        table[totalRow, 2].TextBox.SetText("-");
        table[totalRow, 3].TextBox.SetText("100%");
        table[totalRow, 4].TextBox.SetText("-");

        // Center the table horizontally and position vertically
        var tableWidth = tableShape.Width;
        var tableHeight = tableShape.Height;
        tableShape.X = (SlideWidth - tableWidth) / 2;
        tableShape.Y = ContentY + 40; // Below title with generous padding
    }

    /// <summary>
    /// Adds a centered title text box at the top of the slide.
    /// </summary>
    private void AddTitle(Presentation pres, int slideNumber, string title)
    {
        var shapes = pres.Slide(slideNumber).Shapes;
        shapes.AddShape(
            x: MarginX,
            y: TitleY,
            width: ContentWidth,
            height: TitleHeight,
            Geometry.Rectangle,
            title);

        var titleShape = shapes.Last();
        titleShape.Fill?.SetNoFill();
        titleShape.Outline?.SetNoOutline();

        // Set font color to black (for light mode export) using shape-level API
        titleShape.SetFontColor("000000");

        // Style the text - ShapeCrawler uses SetText/Update APIs
        var textBox = titleShape.TextBox;
        if (textBox != null && textBox.Paragraphs.Count > 0)
        {
            var paragraph = textBox.Paragraphs[0];
            if (paragraph.Portions.Count > 0)
            {
                var portion = paragraph.Portions[0];
                portion.Font.Size = 24;
                portion.Font.IsBold = true;
            }
        }
    }
}
