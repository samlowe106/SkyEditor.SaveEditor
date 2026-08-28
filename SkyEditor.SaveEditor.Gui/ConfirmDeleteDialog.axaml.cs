using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SkyEditor.SaveEditor.Gui;

/// <summary>
/// Generic Yes/Cancel confirmation dialog, shown via <c>await new ConfirmDeleteDialog { Message = "..." }.ShowDialog&lt;bool&gt;(owner)</c>.
/// Closing any other way (Escape, the OS close button) returns false, same as Cancel. Despite the
/// name (kept to avoid churning every call site), this isn't delete-specific -- <see cref="ConfirmText"/>
/// controls the confirm button's own label, so this doubles as the generic "are you sure"
/// dialog for anything that needs one (e.g. the story-flag recruit warning in MainWindow.axaml.cs).
/// </summary>
public partial class ConfirmDeleteDialog : Window
{
    public ConfirmDeleteDialog()
    {
        InitializeComponent();
    }

    public string Message
    {
        get => MessageText.Text ?? "";
        set => MessageText.Text = value;
    }

    public string ConfirmText
    {
        get => ConfirmButton.Content as string ?? "";
        set => ConfirmButton.Content = value;
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
