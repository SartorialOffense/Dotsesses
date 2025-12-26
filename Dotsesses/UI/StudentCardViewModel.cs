namespace Dotsesses.UI;

using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Dotsesses.Messages;
using Dotsesses.Models;

/// <summary>
/// ViewModel for an individual student card in the drill-down area.
/// </summary>
public partial class StudentCardViewModel : ObservableObject
{
    private readonly Action? _clearAction;

    [ObservableProperty]
    private StudentAssessment _assessment;

    [ObservableProperty]
    private string _assignedGrade;

    public StudentCardViewModel(StudentAssessment assessment, string assignedGrade, Action? clearAction = null)
    {
        _assessment = assessment;
        _assignedGrade = assignedGrade;
        _clearAction = clearAction;
    }

    [RelayCommand]
    private void Clear()
    {
        _clearAction?.Invoke();
    }

    /// <summary>
    /// Called when comment editing is complete (e.g., TextBox loses focus).
    /// Sends message to refresh plots showing comment indicators.
    /// </summary>
    [RelayCommand]
    private void CommentChanged()
    {
        WeakReferenceMessenger.Default.Send(new StudentEditedMessage(Assessment.Id));
    }
}
