namespace Dotsesses.Messages;

/// <summary>
/// Message for synchronizing hover state between dotplot and violin plot.
/// </summary>
public class StudentHoverMessage
{
    /// <summary>
    /// The ID of the hovered student, or null if no student is hovered.
    /// </summary>
    public int? StudentId { get; }

    /// <summary>
    /// The source of the hover event ("dotplot" or "violin").
    /// Used to prevent infinite message loops.
    /// </summary>
    public string Source { get; }

    /// <summary>
    /// Screen position of the hover event (optional, for tooltip positioning).
    /// </summary>
    public (double X, double Y)? ScreenPosition { get; }

    public StudentHoverMessage(int? studentId, string source, (double X, double Y)? screenPosition = null)
    {
        StudentId = studentId;
        Source = source;
        ScreenPosition = screenPosition;
    }
}
