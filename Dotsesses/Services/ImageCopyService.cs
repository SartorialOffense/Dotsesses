using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Messaging;
using Dotsesses.Messages;
using Dotsesses.Models;

namespace Dotsesses.Services;

/// <summary>
/// Service for copying controls to clipboard with theme-based rendering for print-friendly output.
/// </summary>
public static class ImageCopyService
{
    /// <summary>
    /// Renders a control to bitmap in LightMode theme and copies to clipboard.
    /// </summary>
    public static async Task CopyControlToClipboardAsync(Control control, IClipboard clipboard)
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

        // Additional delay to ensure visual update is complete
        await Task.Delay(100);

        // Capture the screenshot
        var bounds = control.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            // Switch back to DarkMode before returning
            WeakReferenceMessenger.Default.Send(new RenderWithThemeMessage(ThemeName.DarkMode));
            return;
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

        // Save to temp file and copy to clipboard
        var tempPath = Path.Combine(Path.GetTempPath(), $"dotsesses_copy_{DateTime.Now:HHmmss}.png");
        renderBitmap.Save(tempPath);

        // Copy as file URL (works on macOS)
        var dataObject = new DataObject();
        dataObject.Set(DataFormats.Files, new[] { tempPath });
        await clipboard.SetDataObjectAsync(dataObject);

        // Switch back to DarkMode
        WeakReferenceMessenger.Default.Send(new RenderWithThemeMessage(ThemeName.DarkMode));
    }
}
