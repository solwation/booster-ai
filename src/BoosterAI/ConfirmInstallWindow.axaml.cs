using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace BoosterAI;

/// <summary>Explains the orchestra and asks before an AI installs it in a tab's directory.</summary>
public partial class ConfirmInstallWindow : Window
{
    public const string OrchestraUrl = "https://github.com/solwation/dirigenten-och-orkestern";

    // Required by the XAML previewer.
    public ConfirmInstallWindow() => InitializeComponent();

    public ConfirmInstallWindow(TabViewModel tab)
    {
        DataContext = tab;
        InitializeComponent();
    }

    private void OnConfirm(object? sender, RoutedEventArgs e) => Close(true);
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

    private void OnReadMore(object? sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(OrchestraUrl) { UseShellExecute = true }); }
        catch (Exception) { /* No browser: the URL is in the README. */ }
    }
}
