namespace Dotsesses.Messages;

/// <summary>
/// Sent when background plot initialization (violin / correlation) throws.
/// The view subscribes and surfaces the failure to the user instead of
/// letting the unhandled exception abort the process.
/// </summary>
public class PlotInitErrorMessage
{
    public string PlotName { get; }
    public string ErrorMessage { get; }

    public PlotInitErrorMessage(string plotName, string errorMessage)
    {
        PlotName = plotName;
        ErrorMessage = errorMessage;
    }
}
