using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Dotsesses.UI;

/// <summary>
/// Settings dialog. Hosts a TabControl with the Score Selection table and two
/// commit/dismiss buttons (Apply + dynamic-label Dismiss). Per the slice plan and
/// CommentEditorWindow precedent, dialog dismissal lives in the View layer — the VM
/// commands only emit the commit-or-discard intent, then the click handler closes
/// the window. The Dismiss button's label flips between "Cancel" (dirty) and
/// "Close" (clean) via <see cref="SettingsViewModel.DismissButtonLabel"/>.
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
        // Per R009: Apply commits the draft and runs the recompute pipeline but the
        // dialog stays open so the user can iterate on toggles. The Dismiss button
        // (Cancel/Close, dynamic label) is what closes the dialog.
        if (DataContext is SettingsViewModel vm)
        {
            vm.ApplyCommand.Execute(null);
        }
    }

    private void OnDismissClick(object? sender, RoutedEventArgs e)
    {
        // Single dismiss handler for the dynamic-label Cancel/Close button.
        // The VM command is currently a no-op (commit-or-discard intent only);
        // dismissal lives here in the View layer per the codebase's dialog convention.
        if (DataContext is SettingsViewModel vm)
        {
            vm.DismissCommand.Execute(null);
        }
        Close();
    }
}
