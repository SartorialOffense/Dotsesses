using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Dotsesses.Models;

/// <summary>
/// Represents an individual numeric score component with optional comment.
/// </summary>
public class Score : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private int? _index;
    private double _value;
    private string? _comment;

    public string Name
    {
        get => _name;
        set
        {
            if (_name != value)
            {
                _name = value;
                OnPropertyChanged();
            }
        }
    }

    public int? Index
    {
        get => _index;
        set
        {
            if (_index != value)
            {
                _index = value;
                OnPropertyChanged();
            }
        }
    }

    public double Value
    {
        get => _value;
        set
        {
            if (_value != value)
            {
                _value = value;
                OnPropertyChanged();
            }
        }
    }

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

    /// <summary>
    /// Parameterless constructor for JSON deserialization.
    /// </summary>
    [JsonConstructor]
    public Score() { }

    public Score(string name, int? index, double value, string? comment = null)
    {
        _name = name;
        _index = index;
        _value = value;
        _comment = comment;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
