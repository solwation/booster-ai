using Avalonia.Controls;
using Avalonia.Interactivity;

namespace BoosterAI;

public enum CloseTabChoice { Cancel, Save, Force }

/// <summary>Asks before an unticked running tab that is not resting closes (#272); cancelling keeps it ticked and running.</summary>
public partial class ConfirmCloseTabWindow : Window
{
    // Required by the XAML previewer.
    public ConfirmCloseTabWindow() : this(new CloseTabQuestion("", "", "", true)) { }

    public ConfirmCloseTabWindow(CloseTabQuestion question)
    {
        DataContext = question;
        InitializeComponent();
        Opened += (_, _) => CancelClose.Focus();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(CloseTabChoice.Cancel);
    private void OnConfirm(object? sender, RoutedEventArgs e) => Close(CloseTabChoice.Save);
    private void OnForce(object? sender, RoutedEventArgs e) => Close(CloseTabChoice.Force);
}
