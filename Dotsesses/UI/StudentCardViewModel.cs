namespace Dotsesses.UI;

using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Dotsesses.Messages;
using Dotsesses.Models;

/// <summary>
/// ViewModel for an individual student card in the drill-down area.
/// </summary>
public partial class StudentCardViewModel : ObservableObject, IDisposable
{
    private readonly Action? _clearAction;
    private CancellationTokenSource? _debounce;
    private bool _disposed;

    [ObservableProperty]
    private StudentAssessment _assessment;

    [ObservableProperty]
    private string _assignedGrade;

    public StudentCardViewModel(StudentAssessment assessment, string assignedGrade, Action? clearAction = null)
    {
        _assessment = assessment;
        _assignedGrade = assignedGrade;
        _clearAction = clearAction;

        // Subscribe to comment changes on all scores
        foreach (var score in assessment.Scores)
        {
            score.PropertyChanged += OnScorePropertyChanged;
        }
    }

    private void OnScorePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Score.Comment))
            return;

        // Debounce: cancel previous timer and start new 1s delay
        _debounce?.Cancel();
        _debounce = new CancellationTokenSource();
        var token = _debounce.Token;

        Task.Delay(1000, token).ContinueWith(t =>
        {
            if (!t.IsCanceled)
            {
                // Must send message on UI thread
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    WeakReferenceMessenger.Default.Send(new StudentEditedMessage(Assessment.Id));
                });
            }
        }, token);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Unsubscribe from all scores
        foreach (var score in Assessment.Scores)
        {
            score.PropertyChanged -= OnScorePropertyChanged;
        }

        _debounce?.Cancel();
        _debounce?.Dispose();
    }

    [RelayCommand]
    private void Clear()
    {
        _clearAction?.Invoke();
    }
}
