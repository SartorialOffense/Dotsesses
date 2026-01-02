# Copy Plot to Clipboard Feature

**Date:** 2026-01-01
**Status:** Ready for Implementation

## Overview

Add copy-to-clipboard functionality for both the DotPlot and ViolinPlot controls. The copied image will have inverted grayscale colors (dark background → white background) for printing/document use, while preserving the actual data colors.

## Requirements

1. **Format:** PNG image to system clipboard
2. **UI:** Small copy button in upper-right corner of each plot control
3. **Scope:** Include everything visible - plots, cursors, labels, overlays
4. **Color Inversion:** Only grayscale colors get inverted
   - Grayscale test: R, G, B each within ±10 of the RGB average
   - If grayscale AND average > 127 → invert to dark (255 - avg)
   - If grayscale AND average ≤ 127 → invert to light (255 - avg)
   - Non-grayscale colors (series/data colors) → preserve as-is

## Architecture

### Current Control Structure

**DotPlot (MainWindow.axaml lines 34-49):**
```
Border (Background="#101010")
└── Grid
    ├── PlotView (OxyPlot)
    └── Canvas (DotPlotHoverOverlay)
```

**ViolinPlot (ViolinPlotControl.axaml):**
```
UserControl
└── Grid (ColumnDefinitions="*,70")
    ├── Grid (ViolinPlotArea)
    │   ├── Image (SvgView)
    │   ├── Canvas (RegionBandsOverlay)
    │   ├── Canvas (PointsOverlay)
    │   ├── Canvas (TooltipsOverlay)
    │   └── Canvas (CommentsOverlay)
    └── Canvas (CursorColumnCanvas)
```

### Existing Screenshot Code

Reference: `MainWindow.axaml.cs` lines 461-494 (`SaveSnapshotAsync`)
- Uses `RenderTargetBitmap` with `PixelSize` and `Vector(96, 96)` DPI
- Calls `bitmap.Render(control)` to capture visual tree
- Saves with `bitmap.Save(path, 100)` for PNG

## Implementation Plan

### Step 1: Create ImageCopyService

**File:** `Dotsesses/Services/ImageCopyService.cs`

```csharp
using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;

namespace Dotsesses.Services;

/// <summary>
/// Service for copying controls to clipboard with color inversion for print-friendly output.
/// </summary>
public static class ImageCopyService
{
    private const int GrayscaleTolerance = 10;

    /// <summary>
    /// Renders a control to bitmap, inverts grayscale colors, and copies to clipboard.
    /// </summary>
    public static async Task CopyControlToClipboardAsync(Control control, IClipboard clipboard)
    {
        // Ensure layout is current
        control.UpdateLayout();

        // Small delay to ensure rendering is complete
        await Task.Delay(50);

        var bounds = control.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        var pixelSize = new PixelSize((int)bounds.Width, (int)bounds.Height);
        var dpi = new Vector(96, 96);

        // Render control to bitmap
        using var renderBitmap = new RenderTargetBitmap(pixelSize, dpi);
        renderBitmap.Render(control);

        // Convert to WriteableBitmap for pixel manipulation
        var invertedBitmap = InvertGrayscaleColors(renderBitmap);

        // Save to temp file and copy to clipboard
        var tempPath = Path.Combine(Path.GetTempPath(), $"dotsesses_copy_{DateTime.Now:HHmmss}.png");
        invertedBitmap.Save(tempPath);

        // Copy as file URL (works on macOS)
        var dataObject = new DataObject();
        dataObject.Set(DataFormats.Files, new[] { tempPath });
        await clipboard.SetDataObjectAsync(dataObject);

        invertedBitmap.Dispose();
    }

    /// <summary>
    /// Creates a new bitmap with grayscale colors inverted.
    /// </summary>
    private static WriteableBitmap InvertGrayscaleColors(RenderTargetBitmap source)
    {
        var size = source.PixelSize;
        var writeable = new WriteableBitmap(size, source.Dpi, Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul);

        using (var srcBuffer = source.Lock())
        using (var dstBuffer = writeable.Lock())
        {
            unsafe
            {
                var srcPtr = (byte*)srcBuffer.Address;
                var dstPtr = (byte*)dstBuffer.Address;
                var pixelCount = size.Width * size.Height;

                for (int i = 0; i < pixelCount; i++)
                {
                    var offset = i * 4;
                    byte b = srcPtr[offset];
                    byte g = srcPtr[offset + 1];
                    byte r = srcPtr[offset + 2];
                    byte a = srcPtr[offset + 3];

                    if (IsGrayscale(r, g, b))
                    {
                        var avg = (byte)((r + g + b) / 3);
                        var inverted = (byte)(255 - avg);
                        dstPtr[offset] = inverted;     // B
                        dstPtr[offset + 1] = inverted; // G
                        dstPtr[offset + 2] = inverted; // R
                        dstPtr[offset + 3] = a;        // A
                    }
                    else
                    {
                        // Keep original color
                        dstPtr[offset] = b;
                        dstPtr[offset + 1] = g;
                        dstPtr[offset + 2] = r;
                        dstPtr[offset + 3] = a;
                    }
                }
            }
        }

        return writeable;
    }

    /// <summary>
    /// Determines if a color is grayscale (R, G, B within tolerance of average).
    /// </summary>
    private static bool IsGrayscale(byte r, byte g, byte b)
    {
        var avg = (r + g + b) / 3.0;
        return Math.Abs(r - avg) <= GrayscaleTolerance &&
               Math.Abs(g - avg) <= GrayscaleTolerance &&
               Math.Abs(b - avg) <= GrayscaleTolerance;
    }
}
```

### Step 2: Add Copy Button to DotPlot

**File:** `Dotsesses/UI/MainWindow.axaml`

Modify lines 34-49 to wrap the Grid in another Grid with the copy button overlay:

```xml
<!-- Top Row: Dotplot -->
<Border Grid.Row="1" Background="#101010" CornerRadius="8">
    <Grid>
        <!-- Existing content -->
        <Grid>
            <oxy:PlotView x:Name="DotPlotView" Model="{Binding DotplotModel}" Background="Transparent">
                <!-- cursor binding -->
            </oxy:PlotView>
            <Canvas x:Name="DotPlotHoverOverlay" Background="Transparent" IsHitTestVisible="False" />
        </Grid>

        <!-- Copy button overlay -->
        <Button x:Name="CopyDotPlotButton"
                Click="OnCopyDotPlotClick"
                HorizontalAlignment="Right"
                VerticalAlignment="Top"
                Margin="0,4,4,0"
                Width="28" Height="28"
                Background="#3A3A3C"
                Foreground="White"
                BorderThickness="0"
                CornerRadius="4"
                Opacity="0.7"
                ToolTip.Tip="Copy to clipboard (inverted for print)">
            <TextBlock Text="📋" FontSize="14" HorizontalAlignment="Center" VerticalAlignment="Center"/>
        </Button>
    </Grid>
</Border>
```

### Step 3: Add Click Handler to MainWindow.axaml.cs

**File:** `Dotsesses/UI/MainWindow.axaml.cs`

Add to the constructor or Loaded handler:
```csharp
CopyDotPlotButton.Click += OnCopyDotPlotClick;
```

Add handler method:
```csharp
private async void OnCopyDotPlotClick(object? sender, RoutedEventArgs e)
{
    var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
    if (clipboard == null) return;

    // Get the Border containing the DotPlot (includes overlay)
    var dotPlotContainer = DotPlotView.Parent?.Parent as Control;
    if (dotPlotContainer != null)
    {
        await ImageCopyService.CopyControlToClipboardAsync(dotPlotContainer, clipboard);
    }
}
```

### Step 4: Add Copy Button to ViolinPlotControl

**File:** `Dotsesses/UI/ViolinPlotControl.axaml`

Wrap the existing Grid in another Grid:

```xml
<UserControl ...>
    <Grid>
        <!-- Existing content -->
        <Grid ColumnDefinitions="*,70">
            <!-- ViolinPlotArea -->
            <!-- CursorColumnCanvas -->
        </Grid>

        <!-- Copy button overlay -->
        <Button x:Name="CopyViolinPlotButton"
                Click="OnCopyViolinPlotClick"
                HorizontalAlignment="Right"
                VerticalAlignment="Top"
                Margin="0,4,4,0"
                Width="28" Height="28"
                Background="#3A3A3C"
                Foreground="White"
                BorderThickness="0"
                CornerRadius="4"
                Opacity="0.7"
                ToolTip.Tip="Copy to clipboard (inverted for print)">
            <TextBlock Text="📋" FontSize="14" HorizontalAlignment="Center" VerticalAlignment="Center"/>
        </Button>
    </Grid>
</UserControl>
```

### Step 5: Add Click Handler to ViolinPlotControl.axaml.cs

**File:** `Dotsesses/UI/ViolinPlotControl.axaml.cs`

Add handler:
```csharp
private async void OnCopyViolinPlotClick(object? sender, RoutedEventArgs e)
{
    var topLevel = TopLevel.GetTopLevel(this);
    var clipboard = topLevel?.Clipboard;
    if (clipboard == null) return;

    // Copy the entire control (includes all overlays)
    await ImageCopyService.CopyControlToClipboardAsync(this, clipboard);
}
```

## Files Summary

| File | Action |
|------|--------|
| `Dotsesses/Services/ImageCopyService.cs` | Create new |
| `Dotsesses/UI/MainWindow.axaml` | Modify (add button) |
| `Dotsesses/UI/MainWindow.axaml.cs` | Modify (add handler) |
| `Dotsesses/UI/ViolinPlotControl.axaml` | Modify (add button) |
| `Dotsesses/UI/ViolinPlotControl.axaml.cs` | Modify (add handler) |

## Testing

1. Run application with sample data
2. Click copy button on DotPlot - verify clipboard contains PNG with white background
3. Click copy button on ViolinPlot - verify clipboard contains PNG with white background
4. Verify series colors are preserved (not inverted)
5. Verify all overlays (cursors, labels, hover rings) are captured
6. Test pasting into document application (Word, Preview, etc.)

## Notes

- The clipboard implementation uses file URL format which works reliably on macOS
- Temp files are created in system temp directory with timestamp to avoid conflicts
- Pixel manipulation uses unsafe code for performance
- Button opacity is 0.7 to be visible but not distracting
