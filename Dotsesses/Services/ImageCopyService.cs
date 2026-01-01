using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

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

        // Convert to WriteableBitmap for pixel manipulation
        var invertedBitmap = InvertGrayscaleColors(renderBitmap, pixelSize, dpi);

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
    private static WriteableBitmap InvertGrayscaleColors(RenderTargetBitmap source, PixelSize size, Vector dpi)
    {
        // Calculate buffer size (BGRA = 4 bytes per pixel)
        int stride = size.Width * 4;
        int bufferSize = stride * size.Height;
        var pixels = new byte[bufferSize];

        // Copy pixels from source using pinned array
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            source.CopyPixels(new PixelRect(0, 0, size.Width, size.Height), handle.AddrOfPinnedObject(), bufferSize, stride);
        }
        finally
        {
            handle.Free();
        }

        // Process pixels - composite over black background, then invert grayscale colors
        // Note: pixels are in premultiplied alpha format (BGRA)
        // For print output, we flatten transparency to black, then invert grayscale
        for (int i = 0; i < bufferSize; i += 4)
        {
            byte b = pixels[i];
            byte g = pixels[i + 1];
            byte r = pixels[i + 2];
            byte a = pixels[i + 3];

            // Composite over black background:
            // For premultiplied alpha, the stored RGB values ARE the result of
            // compositing over black (since black contributes nothing).
            // For fully transparent pixels (a=0), the result over black is black (0,0,0).
            byte compositeR = (a == 0) ? (byte)0 : r;
            byte compositeG = (a == 0) ? (byte)0 : g;
            byte compositeB = (a == 0) ? (byte)0 : b;

            // Test if the composited result is grayscale and invert if so
            if (IsGrayscale(compositeR, compositeG, compositeB))
            {
                var avg = (compositeR + compositeG + compositeB) / 3;
                var inverted = (byte)(255 - avg);
                pixels[i] = inverted;     // B
                pixels[i + 1] = inverted; // G
                pixels[i + 2] = inverted; // R
            }
            else
            {
                // Keep the composited color values
                pixels[i] = compositeB;
                pixels[i + 1] = compositeG;
                pixels[i + 2] = compositeR;
            }

            // Make fully opaque (flattened for print)
            pixels[i + 3] = 255;
        }

        // Create WriteableBitmap and copy processed pixels
        var writeable = new WriteableBitmap(size, dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);

        using (var frameBuffer = writeable.Lock())
        {
            System.Runtime.InteropServices.Marshal.Copy(pixels, 0, frameBuffer.Address, bufferSize);
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
