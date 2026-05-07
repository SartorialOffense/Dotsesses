namespace Dotsesses.UI;

using CommunityToolkit.Mvvm.ComponentModel;
using Dotsesses.Models;

/// <summary>
/// UI-binding adapter for one GradeCutoff inside a GradingSession.
/// Slots have stable per-Grade identity for the lifetime of the
/// session (per ADR-0008). Score and IsEnabled setters are internal
/// — only the owning GradingSession mutates them; consumers bind
/// read-only.
/// </summary>
public sealed class CutoffSlot : ObservableObject
{
    private int _score;
    private bool _isEnabled;

    public Grade Grade { get; }

    public int Score
    {
        get => _score;
        internal set => SetProperty(ref _score, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        internal set => SetProperty(ref _isEnabled, value);
    }

    internal CutoffSlot(Grade grade, int score, bool isEnabled)
    {
        ArgumentNullException.ThrowIfNull(grade);
        Grade = grade;
        _score = score;
        _isEnabled = isEnabled;
    }
}
