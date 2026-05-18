namespace Dotsesses.Messages;

using System.Collections.Generic;
using Dotsesses.Models;

/// <summary>
/// Sent by the ViewModel after a successful file load when the reader
/// detected non-fatal issues worth telling the user about. The View
/// renders one combined dialog rather than serial popups.
/// </summary>
public class LoadWarningsMessage
{
    public IReadOnlyList<ReadWarning> Warnings { get; }

    public LoadWarningsMessage(IReadOnlyList<ReadWarning> warnings)
    {
        Warnings = warnings;
    }
}
