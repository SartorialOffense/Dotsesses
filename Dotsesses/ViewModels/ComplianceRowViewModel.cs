namespace Dotsesses.ViewModels;

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
    private int _targetCount;

    [ObservableProperty]
    private int _currentCount;

    [ObservableProperty]
    private bool _isEnabled;

    public int Deviation => Math.Abs(CurrentCount - TargetCount);

    public bool HasDeviation => Deviation > 0;

    public ComplianceRowViewModel(Grade grade, int targetCount, int currentCount, bool isEnabled, Action? onEnabledChanged = null)
    {
        _grade = grade;
        _targetCount = targetCount;
        _currentCount = currentCount;
        _isEnabled = isEnabled;
        _onEnabledChanged = onEnabledChanged;
    }

    private readonly Action? _onEnabledChanged;

    partial void OnCurrentCountChanged(int value)
    {
        OnPropertyChanged(nameof(Deviation));
        OnPropertyChanged(nameof(HasDeviation));
    }

    partial void OnTargetCountChanged(int value)
    {
        OnPropertyChanged(nameof(Deviation));
        OnPropertyChanged(nameof(HasDeviation));
    }

    partial void OnIsEnabledChanged(bool value)
    {
        _onEnabledChanged?.Invoke();
    }
}
