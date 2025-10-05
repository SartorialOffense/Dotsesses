namespace Dotsesses.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using Dotsesses.Models;

/// <summary>
/// ViewModel for an individual grade cursor.
/// </summary>
public partial class CursorViewModel : ObservableObject
{
    [ObservableProperty]
    private Grade _grade;

    [ObservableProperty]
    private int _score;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _isDragging;

    public CursorViewModel(Grade grade, int score, bool isEnabled = true)
    {
        _grade = grade;
        _score = score;
        _isEnabled = isEnabled;
    }
}
