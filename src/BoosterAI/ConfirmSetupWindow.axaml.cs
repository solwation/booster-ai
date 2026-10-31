using Avalonia.Controls;
using Avalonia.Interactivity;

namespace BoosterAI;

/// <summary>
/// Asks before a setup step that changes the system (#74): installing WSL (admin rights, usually a restart) or
/// adding full-auto aliases to ~/.bashrc (the CLIs then act without asking). Cancel is the default button.
/// </summary>
public partial class ConfirmSetupWindow : Window
{
    // Required by the XAML previewer.
    public ConfirmSetupWindow() : this("", "", "", "", "", "", "") { }

    private ConfirmSetupWindow(string heading, string body, string codeLabel, string code, string detail, string button, string cancel)
    {
        (Heading, Body, CodeLabel, Code, Detail, Button, CancelText) = (heading, body, codeLabel, code, detail, button, cancel);
        DataContext = this;
        InitializeComponent();
    }

    public static ConfirmSetupWindow Wsl() => new(Loc.Get("setupConfirm.wsl.heading"), Loc.Get("setupConfirm.wsl.body"),
        Loc.Get("setupConfirm.wsl.code"), Prerequisites.WslInstall.Exe + " " + string.Join(' ', Prerequisites.WslInstall.Args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)),
        Loc.Get("setupConfirm.wsl.detail"), Loc.Get("setupConfirm.wsl.button"), Loc.Get("cancel"));

    public static ConfirmSetupWindow Aliases(IReadOnlyList<string> clis) => new(Loc.Get("setupConfirm.alias.heading"),
        Loc.Format("setupConfirm.alias.body", string.Join(", ", clis)), Loc.Get("setupConfirm.alias.code"),
        Prerequisites.BlockStart + "\n" + string.Join("\n", clis.Select(Prerequisites.AliasLine)) + "\n" + Prerequisites.BlockEnd,
        Loc.Get("setupConfirm.alias.detail"), Loc.Get("setupConfirm.alias.button"), Loc.Get("setup.dismiss"));

    public string Heading { get; }
    public string Body { get; }
    public string CodeLabel { get; }
    public string Code { get; }
    public string Detail { get; }
    public string Button { get; }
    public string CancelText { get; }

    private void OnConfirm(object? sender, RoutedEventArgs e) => Close(true);
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
