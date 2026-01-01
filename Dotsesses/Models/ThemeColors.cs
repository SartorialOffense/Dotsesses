using Avalonia.Media;
using OxyPlot;

namespace Dotsesses.Models;

/// <summary>
/// Provides theme-aware colors for rendering controls.
/// </summary>
public static class ThemeColors
{
    /// <summary>
    /// Primary background color (plot areas, tooltips, etc.)
    /// DarkMode: #101010, LightMode: #FFFFFF
    /// </summary>
    public static Color Background(ThemeName theme) =>
        theme == ThemeName.DarkMode
            ? Color.FromRgb(16, 16, 16)
            : Color.FromRgb(255, 255, 255);

    /// <summary>
    /// Primary foreground/text color.
    /// DarkMode: White, LightMode: Black
    /// </summary>
    public static Color Foreground(ThemeName theme) =>
        theme == ThemeName.DarkMode
            ? Colors.White
            : Colors.Black;

    /// <summary>
    /// Border color for UI elements.
    /// DarkMode: White, LightMode: Black
    /// </summary>
    public static Color Border(ThemeName theme) =>
        theme == ThemeName.DarkMode
            ? Colors.White
            : Colors.Black;

    /// <summary>
    /// Secondary/muted text color.
    /// DarkMode: #AAAAAA, LightMode: #555555
    /// </summary>
    public static Color SecondaryText(ThemeName theme) =>
        theme == ThemeName.DarkMode
            ? Color.FromRgb(170, 170, 170)
            : Color.FromRgb(85, 85, 85);

    /// <summary>
    /// Semi-transparent line color (for cursor lines, etc.)
    /// </summary>
    public static Color TransparentLine(ThemeName theme) =>
        theme == ThemeName.DarkMode
            ? Color.FromArgb(128, 255, 255, 255)
            : Color.FromArgb(128, 0, 0, 0);

    /// <summary>
    /// Brush helpers for convenience.
    /// </summary>
    public static SolidColorBrush BackgroundBrush(ThemeName theme) => new(Background(theme));
    public static SolidColorBrush ForegroundBrush(ThemeName theme) => new(Foreground(theme));
    public static SolidColorBrush BorderBrush(ThemeName theme) => new(Border(theme));
    public static SolidColorBrush SecondaryTextBrush(ThemeName theme) => new(SecondaryText(theme));
    public static SolidColorBrush TransparentLineBrush(ThemeName theme) => new(TransparentLine(theme));

    // ===== OxyPlot Color Helpers =====

    /// <summary>
    /// OxyPlot background color.
    /// </summary>
    public static OxyColor OxyBackground(ThemeName theme) =>
        theme == ThemeName.DarkMode
            ? OxyColors.Transparent  // Let Avalonia background show through
            : OxyColor.FromRgb(255, 255, 255);

    /// <summary>
    /// OxyPlot foreground/text color.
    /// </summary>
    public static OxyColor OxyForeground(ThemeName theme) =>
        theme == ThemeName.DarkMode
            ? OxyColors.White
            : OxyColors.Black;

    /// <summary>
    /// OxyPlot border/grid line color.
    /// </summary>
    public static OxyColor OxyBorder(ThemeName theme) =>
        theme == ThemeName.DarkMode
            ? OxyColor.FromRgb(60, 60, 60)
            : OxyColor.FromRgb(200, 200, 200);

    /// <summary>
    /// OxyPlot secondary/muted text color.
    /// </summary>
    public static OxyColor OxySecondaryText(ThemeName theme) =>
        theme == ThemeName.DarkMode
            ? OxyColor.FromRgb(180, 180, 180)
            : OxyColor.FromRgb(100, 100, 100);

    /// <summary>
    /// OxyPlot semi-transparent line color.
    /// </summary>
    public static OxyColor OxyTransparentLine(ThemeName theme) =>
        theme == ThemeName.DarkMode
            ? OxyColor.FromAColor(128, OxyColors.White)
            : OxyColor.FromAColor(128, OxyColors.Black);

    /// <summary>
    /// OxyPlot handle fill color.
    /// </summary>
    public static OxyColor OxyHandleFill(ThemeName theme) =>
        theme == ThemeName.DarkMode
            ? OxyColors.Black
            : OxyColors.White;

    /// <summary>
    /// OxyPlot handle stroke color.
    /// </summary>
    public static OxyColor OxyHandleStroke(ThemeName theme) =>
        theme == ThemeName.DarkMode
            ? OxyColors.White
            : OxyColors.Black;
}
