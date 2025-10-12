namespace Dotsesses.UI;

using CommunityToolkit.Mvvm.ComponentModel;
using Dotsesses.Models;

/// <summary>
/// ViewModel for an individual student card in the drill-down area.
/// </summary>
public partial class StudentCardViewModel : ObservableObject
{
    [ObservableProperty]
    private StudentAssessment _assessment;

    [ObservableProperty]
    private string _assignedGrade;

    public StudentCardViewModel(StudentAssessment assessment, string assignedGrade)
    {
        _assessment = assessment;
        _assignedGrade = assignedGrade;
    }
}
