using CommunityToolkit.Mvvm.ComponentModel;

namespace Dotsesses.ViewModels;

/// <summary>
/// ViewModel for the comment editor window.
/// </summary>
public partial class CommentEditorViewModel : ViewModelBase
{
    [ObservableProperty]
    private string? _comment;

    public string StudentName { get; }

    public CommentEditorViewModel(string studentName, string? existingComment)
    {
        StudentName = studentName;
        Comment = existingComment;
    }
}
