using Avalonia.Controls;
using Avalonia.Interactivity;

namespace BoosterAI;

/// <summary>Confirms removing configuration for an inactive project group; cancellation is the default.</summary>
public partial class ConfirmRemoveProjectWindow : Window
{
    public ConfirmRemoveProjectWindow() : this(new ProjectRemovalConfirmation("", 0)) { }

    public ConfirmRemoveProjectWindow(ProjectRemovalConfirmation request)
    {
        DataContext = request;
        InitializeComponent();
        Opened += (_, _) => CancelRemoval.Focus();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
    private void OnConfirm(object? sender, RoutedEventArgs e) => Close(true);
}
