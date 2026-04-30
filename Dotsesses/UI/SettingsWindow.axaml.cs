using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Dotsesses.UI;

/// <summary>
/// Settings dialog. Hosts a TabControl with the Score Selection table and three
/// commit/dismiss buttons. Per the slice plan and CommentEditorWindow precedent,
/// dialog dismissal lives in the View layer — the VM commands only emit the
/// commit-or-discard intent, then the click handler closes the window.
/// </summary>
public partial class SettingsWindow : Window
{
    /// <summary>
    /// Parameterless constructor required for the XAML compiler / previewer.
    /// Production callers should use the SettingsViewModel-accepting overload.
    /// </summary>
    public SettingsWindow()
    {
        InitializeComponent();
    }

    public SettingsWindow(SettingsViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    private void OnApplyClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            vm.ApplyCommand.Execute(null);
        }
        Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            vm.CancelCommand.Execute(null);
        }
        Close();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            vm.CloseCommand.Execute(null);
        }
        Close();
    }
}
