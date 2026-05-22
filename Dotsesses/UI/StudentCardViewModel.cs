namespace Dotsesses.UI;

using System;
using System.Collections.Generic;
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
    private readonly IMessenger _messenger;
    private CancellationTokenSource? _debounce;
    private bool _disposed;

    [ObservableProperty]
    private StudentAssessment _assessment;

    [ObservableProperty]
    private string _assignedGrade;

    /// <summary>
    /// Series name to hex color map for coloring score labels and comment borders.
    /// </summary>
    public Dictionary<string, string> SeriesColorMap { get; }

    /// <summary>
    /// The score list shown in the drill-down score table. May be a filtered subset
    /// of <c>Assessment.Scores</c> (S04: respects Display selections from the Settings
    /// dialog). When the constructor receives a null filter, defaults to the full
    /// score list for backward-compat. Note: PropertyChanged subscriptions cover the
    /// full <c>Assessment.Scores</c> list so hidden-but-still-comment-editable scores
    /// continue to fire StudentEditedMessage.
    /// </summary>
    public IReadOnlyList<Score> DisplayScores { get; }

    public StudentCardViewModel(StudentAssessment assessment, string assignedGrade, Dictionary<string, string> seriesColorMap, IMessenger messenger, Action? clearAction = null, IReadOnlyList<Score>? displayScores = null)
    {
        _assessment = assessment;
        _assignedGrade = assignedGrade;
        SeriesColorMap = seriesColorMap;
        _messenger = messenger;
        _clearAction = clearAction;
        DisplayScores = displayScores ?? assessment.Scores;

        // Subscribe to comment changes on ALL scores (not just DisplayScores) — hidden
        // scores must still trigger StudentEditedMessage when their comment is edited.
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
                    _messenger.Send(new StudentEditedMessage(Assessment.Id));
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
