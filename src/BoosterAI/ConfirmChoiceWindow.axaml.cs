using Avalonia.Controls;
using Avalonia.Interactivity;

namespace BoosterAI;

/// <summary>A two-button question (#279): the model switch, its unknown-activity check and turning on the automatic switch. Cancel is the default.</summary>
public partial class ConfirmChoiceWindow : Window
{
    // Required by the XAML previewer.
    public ConfirmChoiceWindow() : this(new ChoiceQuestion("", "", "", "", "")) { }

    public ConfirmChoiceWindow(ChoiceQuestion question)
    {
        DataContext = question;
        InitializeComponent();
        Opened += (_, _) => CancelChoice.Focus();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
    private void OnConfirm(object? sender, RoutedEventArgs e) => Close(true);
}
