using Avalonia.Controls;
using Avalonia.Interactivity;
using Dotsesses.ViewModels;

namespace Dotsesses.Views;

public partial class CommentEditorWindow : Window
{
    public bool WasOkClicked { get; private set; }

    public CommentEditorWindow()
    {
        InitializeComponent();
    }

    public CommentEditorWindow(string studentName, string? existingComment) : this()
    {
        DataContext = new CommentEditorViewModel(studentName, existingComment);

        // Focus the text box when window loads
        Loaded += (s, e) => CommentTextBox.Focus();
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        WasOkClicked = true;
        Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        WasOkClicked = false;
        Close();
    }

    public string? GetComment()
    {
        if (DataContext is CommentEditorViewModel vm)
        {
            return vm.Comment;
        }
        return null;
    }
}
