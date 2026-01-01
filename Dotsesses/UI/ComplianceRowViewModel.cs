namespace Dotsesses.UI;

using CommunityToolkit.Mvvm.ComponentModel;
using Dotsesses.Models;

/// <summary>
/// ViewModel for a single row in the compliance grid.
/// </summary>
public partial class ComplianceRowViewModel : ObservableObject
{
    [ObservableProperty]
    private Grade _grade;

    [ObservableProperty]
    private int _lowerTarget;

    [ObservableProperty]
    private int _upperTarget;

    [ObservableProperty]
    private int _currentCount;

    [ObservableProperty]
    private bool _isEnabled;

    /// <summary>
    /// Display format for target range: "L-U" (e.g., "3-7"), or blank for 0-0.
    /// </summary>
    public string TargetRange => LowerTarget == 0 && UpperTarget == 0 ? "" : $"{LowerTarget}-{UpperTarget}";

    /// <summary>
    /// Signed deviation from the violated bound.
    /// Negative if below lower bound, positive if above upper bound, zero if in range.
    /// </summary>
    public int SignedDeviation
    {
        get
        {
            if (CurrentCount < LowerTarget) return CurrentCount - LowerTarget;
            if (CurrentCount > UpperTarget) return CurrentCount - UpperTarget;
            return 0;
        }
    }

    /// <summary>
    /// True if current count is outside the target range.
    /// </summary>
    public bool HasDeviation => SignedDeviation != 0;

    public ComplianceRowViewModel(
        Grade grade,
        int lowerTarget,
        int upperTarget,
        int currentCount,
        bool isEnabled,
        Action? onEnabledChanged = null)
    {
        _grade = grade;
        _lowerTarget = lowerTarget;
        _upperTarget = upperTarget;
        _currentCount = currentCount;
        _isEnabled = isEnabled;
        _onEnabledChanged = onEnabledChanged;
    }

    private readonly Action? _onEnabledChanged;

    partial void OnCurrentCountChanged(int value)
    {
        OnPropertyChanged(nameof(SignedDeviation));
        OnPropertyChanged(nameof(HasDeviation));
    }

    partial void OnLowerTargetChanged(int value)
    {
        OnPropertyChanged(nameof(TargetRange));
        OnPropertyChanged(nameof(SignedDeviation));
        OnPropertyChanged(nameof(HasDeviation));
    }

    partial void OnUpperTargetChanged(int value)
    {
        OnPropertyChanged(nameof(TargetRange));
        OnPropertyChanged(nameof(SignedDeviation));
        OnPropertyChanged(nameof(HasDeviation));
    }

    partial void OnIsEnabledChanged(bool value)
    {
        _onEnabledChanged?.Invoke();
    }
}
