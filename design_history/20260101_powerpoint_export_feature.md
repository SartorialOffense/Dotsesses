# PowerPoint Export Feature

**Date:** 2026-01-01
**Status:** Design Complete - Ready for Implementation

## Overview

Add PowerPoint (PPTX) export functionality to Dotsesses, creating a presentation with:
1. **Slide 1**: Full-width DotPlot image with title
2. **Slide 2**: Full-width ViolinPlot image with title
3. **Slide 3**: Grade breakdown table with title

All slides use LightMode theme (white background) matching the existing copy-to-clipboard behavior.

## Library Selection: ShapeCrawler

**Package:** `ShapeCrawler` (NuGet)
**Version:** 0.77.1 (latest as of Dec 31, 2025)
**License:** MIT - fully permissive for commercial use

### Why ShapeCrawler

| Requirement | ShapeCrawler Support |
|-------------|---------------------|
| Create PPTX from scratch | ✓ `new Presentation(p => p.Slide())` |
| Add images with position/size | ✓ `AddPicture()` + `X/Y/Width/Height` properties |
| Create tables | ✓ `AddTable(x, y, cols, rows)` |
| No Office required | ✓ Uses Open XML SDK only |
| macOS ARM compatible | ✓ Since v0.61.0 |
| .NET 9.0 compatible | ✓ Cross-platform |
| Actively maintained | ✓ 12+ releases in 2025 |

### Verified API (from source code)

**Adding Images:**
```csharp
// AddPicture only takes stream - position/size set afterward
shapes.AddPicture(imageStream);
var picture = shapes.Last();
picture.X = 50;       // decimal, in points
picture.Y = 100;      // decimal, in points
picture.Width = 860;  // decimal, in points
picture.Height = 420; // decimal, in points
```

**Adding Tables:**
```csharp
shapes.AddTable(x: 50, y: 120, columnsCount: 4, rowsCount: 6);
var table = shapes.Last().Table;
table[0, 0].TextBox.SetText("Header");  // row, col indexing
```

**Adding Shapes (for titles):**
```csharp
shapes.AddShape(x: 50, y: 30, width: 860, height: 50, Geometry.Rectangle, "Title Text");
```

**Slide Dimensions:**
- Standard 16:9 slide: **960 × 540 points**
- Full-width with margins: ~860pt width (50pt margins each side)

## Architecture

### Existing Infrastructure to Leverage

1. **ImageCopyService.CopyControlToClipboardAsync()**
   - Already handles LightMode theme switching via `RenderWithThemeMessage`
   - Uses `RenderTargetBitmap` with display scaling support
   - Saves PNG to temp file

2. **ExportService**
   - Existing pattern for Excel export
   - Has access to `ComplianceRowViewModel` data for table

3. **ThemeColors / RenderWithThemeMessage**
   - Theme switching mechanism already working
   - LightMode: white background, black text

### New Components

```
Services/
├── ImageCopyService.cs       (modify: extract RenderControlToPngStream)
├── ExportService.cs          (existing)
└── PowerPointExportService.cs (NEW)

UI/
├── MainWindow.axaml          (modify: add Export PPTX button)
└── MainWindow.axaml.cs       (modify: add click handler)
```

## Implementation Plan

### Phase 1: Package & Refactor

#### Step 1.1: Add NuGet Package

```bash
cd Dotsesses
dotnet add package ShapeCrawler
```

#### Step 1.2: Refactor ImageCopyService

Extract PNG rendering to reusable method:

```csharp
// Services/ImageCopyService.cs

/// <summary>
/// Renders a control to PNG stream in LightMode theme.
/// </summary>
public static async Task<MemoryStream> RenderControlToPngStreamAsync(Control control)
{
    // Ensure layout is current
    control.UpdateLayout();

    // Switch to LightMode and wait for re-render
    var renderComplete = new TaskCompletionSource<bool>();
    WeakReferenceMessenger.Default.Send(new RenderWithThemeMessage(
        ThemeName.LightMode,
        () => renderComplete.TrySetResult(true)));

    // Wait for render to complete (with timeout)
    var timeoutTask = Task.Delay(500);
    await Task.WhenAny(renderComplete.Task, timeoutTask);
    await Task.Delay(100);

    var bounds = control.Bounds;
    if (bounds.Width <= 0 || bounds.Height <= 0)
    {
        WeakReferenceMessenger.Default.Send(new RenderWithThemeMessage(ThemeName.DarkMode));
        throw new InvalidOperationException("Control has no size");
    }

    var topLevel = TopLevel.GetTopLevel(control);
    var scaling = topLevel?.RenderScaling ?? 1.0;

    var pixelWidth = (int)(bounds.Width * scaling);
    var pixelHeight = (int)(bounds.Height * scaling);
    var pixelSize = new PixelSize(pixelWidth, pixelHeight);
    var dpi = new Vector(96 * scaling, 96 * scaling);

    using var renderBitmap = new RenderTargetBitmap(pixelSize, dpi);
    renderBitmap.Render(control);

    // Save to memory stream
    var stream = new MemoryStream();
    renderBitmap.Save(stream);
    stream.Position = 0;

    // Switch back to DarkMode
    WeakReferenceMessenger.Default.Send(new RenderWithThemeMessage(ThemeName.DarkMode));

    return stream;
}

/// <summary>
/// Copies control to clipboard (existing method, refactored to use above).
/// </summary>
public static async Task CopyControlToClipboardAsync(Control control, IClipboard clipboard)
{
    using var stream = await RenderControlToPngStreamAsync(control);

    var tempPath = Path.Combine(Path.GetTempPath(), $"dotsesses_copy_{DateTime.Now:HHmmss}.png");
    using (var fileStream = File.Create(tempPath))
    {
        await stream.CopyToAsync(fileStream);
    }

    var dataObject = new DataObject();
    dataObject.Set(DataFormats.Files, new[] { tempPath });
    await clipboard.SetDataObjectAsync(dataObject);
}
```

### Phase 2: PowerPointExportService

#### Step 2.1: Create Service

```csharp
// Services/PowerPointExportService.cs

using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Dotsesses.UI;
using ShapeCrawler;

namespace Dotsesses.Services;

/// <summary>
/// Service for exporting presentation slides with plots and grade data.
/// </summary>
public class PowerPointExportService
{
    // Slide dimensions (16:9 standard)
    private const int SlideWidth = 960;
    private const int SlideHeight = 540;

    // Layout constants (points)
    private const int MarginX = 50;
    private const int TitleY = 25;
    private const int TitleHeight = 45;
    private const int ContentY = 80;
    private const int ContentWidth = 860;  // SlideWidth - 2*MarginX

    /// <summary>
    /// Exports a complete presentation with DotPlot, ViolinPlot, and grade table.
    /// </summary>
    public async Task ExportAsync(
        string outputPath,
        Control dotPlotControl,
        Control violinPlotControl,
        IEnumerable<ComplianceRowViewModel> complianceRows,
        string className = "Grade Analysis")
    {
        var pres = new Presentation(p => p.Slide());

        // Slide 1: DotPlot
        await AddPlotSlideAsync(pres, 1, $"{className} - Score Distribution", dotPlotControl);

        // Slide 2: ViolinPlot
        pres.Slides.Add();
        await AddPlotSlideAsync(pres, 2, $"{className} - Component Analysis", violinPlotControl);

        // Slide 3: Grade Table
        pres.Slides.Add();
        AddGradeTableSlide(pres, 3, $"{className} - Grade Breakdown", complianceRows);

        pres.Save(outputPath);
    }

    /// <summary>
    /// Adds a slide with a title and full-width plot image.
    /// </summary>
    private async Task AddPlotSlideAsync(
        Presentation pres,
        int slideNumber,
        string title,
        Control plotControl)
    {
        var shapes = pres.Slide(slideNumber).Shapes;

        // Add title shape
        shapes.AddShape(
            x: MarginX,
            y: TitleY,
            width: ContentWidth,
            height: TitleHeight,
            Geometry.Rectangle,
            title);

        // Style title (larger font would be set here if ShapeCrawler supports it)
        var titleShape = shapes.Last();
        // titleShape.TextBox... (font styling if needed)

        // Render plot to PNG stream
        using var imageStream = await ImageCopyService.RenderControlToPngStreamAsync(plotControl);

        // Add plot image
        shapes.AddPicture(imageStream);
        var picture = shapes.Last();

        // Position and size to fill slide width
        picture.X = MarginX;
        picture.Y = ContentY;
        picture.Width = ContentWidth;

        // Calculate height to maintain aspect ratio or use fixed height
        // For now, use remaining slide height minus margins
        picture.Height = SlideHeight - ContentY - 30; // 30pt bottom margin
    }

    /// <summary>
    /// Adds a slide with a title and grade breakdown table.
    /// </summary>
    private void AddGradeTableSlide(
        Presentation pres,
        int slideNumber,
        string title,
        IEnumerable<ComplianceRowViewModel> complianceRows)
    {
        var shapes = pres.Slide(slideNumber).Shapes;

        // Add title shape
        shapes.AddShape(
            x: MarginX,
            y: TitleY,
            width: ContentWidth,
            height: TitleHeight,
            Geometry.Rectangle,
            title);

        // Filter to enabled grades, sorted by order (A first)
        var rows = complianceRows
            .Where(r => r.IsEnabled)
            .OrderBy(r => r.Grade.Order)
            .ToList();

        // Create table: 5 columns, (rows + 1 header) rows
        // Columns: Grade | Count | Target Range | Percentage | Delta
        var columnCount = 5;
        var rowCount = rows.Count + 1;

        shapes.AddTable(
            x: MarginX,
            y: ContentY,
            columnsCount: columnCount,
            rowsCount: rowCount);

        var table = shapes.Last().Table;

        // Header row
        table[0, 0].TextBox.SetText("Grade");
        table[0, 1].TextBox.SetText("Count");
        table[0, 2].TextBox.SetText("Target");
        table[0, 3].TextBox.SetText("Percentage");
        table[0, 4].TextBox.SetText("Delta");

        // Data rows
        var totalStudents = rows.Sum(r => r.CurrentCount);
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
    }
}
```

### Phase 3: UI Integration

#### Step 3.1: Add Export Button

In `MainWindow.axaml`, add a button near the existing export button:

```xml
<!-- In the toolbar/button area -->
<Button x:Name="ExportPowerPointButton"
        Content="Export PPTX"
        Click="OnExportPowerPointClick"
        Margin="8,0,0,0"
        ToolTip.Tip="Export presentation with plots and grade table"/>
```

#### Step 3.2: Add Click Handler

In `MainWindow.axaml.cs`:

```csharp
private async void OnExportPowerPointClick(object? sender, RoutedEventArgs e)
{
    var topLevel = TopLevel.GetTopLevel(this);
    if (topLevel == null) return;

    // Get save location
    var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
    {
        Title = "Export PowerPoint Presentation",
        DefaultExtension = "pptx",
        SuggestedFileName = $"{_loadedFileName ?? "grades"}-Presentation.pptx",
        FileTypeChoices = new[]
        {
            new FilePickerFileType("PowerPoint Presentation") { Patterns = new[] { "*.pptx" } }
        }
    });

    if (file == null) return;

    var outputPath = file.Path.LocalPath;
    var vm = DataContext as MainWindowViewModel;
    if (vm == null) return;

    try
    {
        var exportService = new PowerPointExportService();
        await exportService.ExportAsync(
            outputPath,
            DotPlotContainer,           // The Border containing the DotPlot
            ViolinPlotControl,          // The ViolinPlotControl instance
            vm.ComplianceRows,
            Path.GetFileNameWithoutExtension(_loadedFileName) ?? "Class");

        // Open the file
        Process.Start(new ProcessStartInfo(outputPath) { UseShellExecute = true });
    }
    catch (Exception ex)
    {
        // Show error dialog
        await MessageBox.ShowAsync(this, $"Export failed: {ex.Message}", "Export Error");
    }
}
```

## File Changes Summary

| File | Action | Description |
|------|--------|-------------|
| `Dotsesses.csproj` | Modify | Add ShapeCrawler package reference |
| `Services/ImageCopyService.cs` | Modify | Extract `RenderControlToPngStreamAsync` method |
| `Services/PowerPointExportService.cs` | **Create** | New service for PPTX generation |
| `UI/MainWindow.axaml` | Modify | Add Export PPTX button |
| `UI/MainWindow.axaml.cs` | Modify | Add export click handler |

## Testing Plan

1. **Package Installation**
   - Verify ShapeCrawler installs without conflicts
   - Check for .NET 9.0 compatibility warnings

2. **Image Rendering**
   - Verify `RenderControlToPngStreamAsync` produces valid PNG
   - Confirm LightMode theme is applied
   - Check display scaling is handled correctly

3. **Slide Generation**
   - Open exported PPTX in PowerPoint/Keynote/LibreOffice
   - Verify all 3 slides are present
   - Check titles are readable
   - Confirm plots are full-width and proportional
   - Validate table data matches UI

4. **Edge Cases**
   - Export with no students loaded
   - Export with some grades disabled
   - Export with very long class name

## Future Enhancements (Out of Scope)

- Custom slide template support
- Additional slide types (per-student breakdown)
- Chart generation (native PowerPoint charts instead of images)
- Presenter notes
- Slide master/theme customization

## Notes

- ShapeCrawler uses **1-based indexing** for slides: `pres.Slide(1)` not `pres.Slide(0)`
- All measurements are in **points** (1 point = 1/72 inch)
- Standard 16:9 slide is 960×540 points
- The `AddPicture()` method only accepts a stream; position/size must be set afterward via properties
