using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Dotsesses.Messages;
using Dotsesses.Models;

namespace Dotsesses.Services;

/// <summary>
/// Service for rendering controls to images with theme-based rendering for print-friendly output.
/// </summary>
public static class ImageCopyService
{
    /// <summary>
    /// Renders a control to PNG stream in LightMode theme.
    /// The caller is responsible for disposing the returned stream.
    /// </summary>
    /// <param name="control">The control to render</param>
    /// <param name="restoreTheme">If true, restores DarkMode after rendering. Set to false when batching multiple renders.</param>
    /// <returns>A MemoryStream containing the PNG image data</returns>
    public static async Task<MemoryStream> RenderControlToPngStreamAsync(Control control, bool restoreTheme = true)
    {
        // Switch to LightMode and wait for re-render
        var renderComplete = new TaskCompletionSource<bool>();
        WeakReferenceMessenger.Default.Send(new RenderWithThemeMessage(
            ThemeName.LightMode,
            () => renderComplete.TrySetResult(true)));

        // Wait for render callback (with timeout)
        var timeoutTask = Task.Delay(1000);
        await Task.WhenAny(renderComplete.Task, timeoutTask);

        // Force layout update on UI thread
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            control.UpdateLayout();
            control.InvalidateVisual();
        });

        // Wait for multiple render frames to ensure all async updates complete
        // This is critical for OxyPlot, Python-generated SVGs, and Canvas overlays
        await Task.Delay(500);

        // Force another layout pass
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            control.UpdateLayout();
        });

        // Capture the screenshot
        var bounds = control.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            if (restoreTheme)
            {
                WeakReferenceMessenger.Default.Send(new RenderWithThemeMessage(ThemeName.DarkMode));
            }
            throw new InvalidOperationException("Control has zero size and cannot be rendered");
        }

        // Get the actual display scaling (e.g., 2.0 for Retina displays)
        var topLevel = TopLevel.GetTopLevel(control);
        var scaling = topLevel?.RenderScaling ?? 1.0;

        // Calculate pixel size accounting for display scaling
        var pixelWidth = (int)(bounds.Width * scaling);
        var pixelHeight = (int)(bounds.Height * scaling);
        var pixelSize = new PixelSize(pixelWidth, pixelHeight);

        // Use scaled DPI for proper rendering
        var dpi = new Vector(96 * scaling, 96 * scaling);

        // Render control to bitmap at full resolution
        using var renderBitmap = new RenderTargetBitmap(pixelSize, dpi);
        renderBitmap.Render(control);

        // Save to memory stream
        var stream = new MemoryStream();
        renderBitmap.Save(stream);
        stream.Position = 0;

        // Restore theme if requested
        if (restoreTheme)
        {
            WeakReferenceMessenger.Default.Send(new RenderWithThemeMessage(ThemeName.DarkMode));
        }

        return stream;
    }

    /// <summary>
    /// Restores the application to DarkMode theme.
    /// Call this after batching multiple renders with restoreTheme=false.
    /// </summary>
    public static void RestoreDarkMode()
    {
        WeakReferenceMessenger.Default.Send(new RenderWithThemeMessage(ThemeName.DarkMode));
    }

    /// <summary>
    /// Renders a control to bitmap in LightMode theme and copies to clipboard.
    /// </summary>
    public static async Task CopyControlToClipboardAsync(Control control, IClipboard clipboard)
    {
        using var stream = await RenderControlToPngStreamAsync(control);

        // Save to temp file and copy to clipboard
        var tempPath = Path.Combine(Path.GetTempPath(), $"dotsesses_copy_{DateTime.Now:HHmmss}.png");
        await using (var fileStream = File.Create(tempPath))
        {
            await stream.CopyToAsync(fileStream);
        }

        // Copy as file URL (works on macOS)
        var dataObject = new DataObject();
        dataObject.Set(DataFormats.Files, new[] { tempPath });
        await clipboard.SetDataObjectAsync(dataObject);
    }
}
