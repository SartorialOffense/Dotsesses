using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Dotsesses.Models;

/// <summary>
/// Represents an individual numeric score component with optional comment.
/// </summary>
public class Score : INotifyPropertyChanged
{
    private string? _comment;

    public string Name { get; }
    public int? Index { get; }
    public double Value { get; }

    /// <summary>
    /// Optional comment/notes for this score.
    /// </summary>
    public string? Comment
    {
        get => _comment;
        set
        {
            if (_comment != value)
            {
                _comment = value;
                OnPropertyChanged();
            }
        }
    }

    public Score(string name, int? index, double value, string? comment = null)
    {
        Name = name;
        Index = index;
        Value = value;
        _comment = comment;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
