using Dotsesses.Models;

namespace Dotsesses.Messages;

/// <summary>
/// Message requesting controls to re-render with a specific theme.
/// Used for capturing print-friendly screenshots.
/// </summary>
public class RenderWithThemeMessage
{
    /// <summary>
    /// The theme to render with.
    /// </summary>
    public ThemeName Theme { get; }

    /// <summary>
    /// Optional callback to invoke after rendering is complete.
    /// </summary>
    public Action? OnRenderComplete { get; }

    public RenderWithThemeMessage(ThemeName theme, Action? onRenderComplete = null)
    {
        Theme = theme;
        OnRenderComplete = onRenderComplete;
    }
}
