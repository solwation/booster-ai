using Avalonia.Controls;
using Avalonia.Interactivity;

namespace BoosterAI;

/// <summary>
/// Confirms system changes and permission alias replacements, including their scope.
/// Cancel is the default button.
/// </summary>
public partial class ConfirmSetupWindow : Window
{
    // Required by the XAML previewer.
    public ConfirmSetupWindow() : this("", "", "", "", "", "", "") { }

    private ConfirmSetupWindow(string heading, string body, string codeLabel, string code, string detail, string button, string cancel,
        bool codeIsPaths = false, string detailPath = "")
    {
        (Heading, Body, CodeLabel, Code, Detail, Button, CancelText) = (heading, body, codeLabel, code, detail, button, cancel);
        (CodeIsPaths, DetailPath) = (codeIsPaths, detailPath);
        DataContext = this;
        InitializeComponent();
    }

    public static ConfirmSetupWindow Wsl() => new(Loc.Get("setupConfirm.wsl.heading"), Loc.Get("setupConfirm.wsl.body"),
        Loc.Get("setupConfirm.wsl.code"), Prerequisites.WslInstall.Exe + " " + string.Join(' ', Prerequisites.WslInstall.Args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)),
        Loc.Get("setupConfirm.wsl.detail"), Loc.Get("setupConfirm.wsl.button"), Loc.Get("cancel"));

    public static ConfirmSetupWindow ShutdownWsl() => new(Loc.Get("shutdownWsl.heading"), Loc.Get("shutdownWsl.body"),
        Loc.Get("shutdownWsl.code"), "wsl --shutdown", Loc.Get("shutdownWsl.detail"),
        Loc.Get("settings.optimization.restartWsl"), Loc.Get("cancel"));

    public static ConfirmSetupWindow Aliases(IReadOnlyList<string> clis) => new(Loc.Get("setupConfirm.alias.heading"),
        Loc.Format("setupConfirm.alias.body", string.Join(", ", clis)), Loc.Get("setupConfirm.alias.code"),
        Prerequisites.BlockStart + "\n" + string.Join("\n", clis.Select(Prerequisites.AliasLine)) + "\n" + Prerequisites.BlockEnd,
        Loc.Get("setupConfirm.alias.detail"), Loc.Get("setupConfirm.alias.button"), Loc.Get("setup.dismiss"));

    public static ConfirmSetupWindow AgentMode(AgentInfo agent) => new(
        Loc.Format("agents.alias.confirm.title", agent.Name), Loc.Get(agent.HasExternalDefinition ? "agents.alias.confirm.replace" : "agents.alias.confirm.apply"),
        Loc.Get("agents.alias.confirm.preview"), (agent.CurrentAliasCommand.Length > 0 ? agent.CurrentAliasCommand : agent.CurrentModeText)
            + "\n\n→\n\n" + agent.ModePreview,
        (agent.IsYolo ? Loc.Get("agents.yolo.warning") + "\n\n" : "") + agent.ModeExplanation + "\n\n" + Loc.Get("agents.alias.scope"),
        Loc.Get("agents.mode.apply"), Loc.Get("cancel"));

    /// <summary>The uninstall confirmation (#131): what goes, and that the config stays.</summary>
    public static ConfirmSetupWindow Uninstall(InstallPlan plan) => new(Loc.Get("uninstall.heading"), Loc.Get("uninstall.body"),
        Loc.Get("uninstall.code"), string.Join("\n", [plan.InstallDir, .. plan.Shortcuts.Select(s => s.Path), "HKCU\\" + plan.UninstallKey]),
        Loc.Get("uninstall.detail"), Loc.Get("uninstall.button"), Loc.Get("cancel"),
        // Paths stay on one line each (trimmed, full text in a tooltip), in a window wide enough for the usual ones.
        codeIsPaths: true, detailPath: plan.ConfigPath) { Width = 720 };

    /// <summary>True once the confirm button closed the window (for a window shown without an owner).</summary>
    public bool Confirmed { get; private set; }

    public string Heading { get; }
    public string Body { get; }
    public string CodeLabel { get; }
    public string Code { get; }
    public string Detail { get; }
    /// <summary>The code block is a list of paths: one unwrapped line each instead of wrapped text.</summary>
    public bool CodeIsPaths { get; }
    public IReadOnlyList<string> CodeLines => Code.Split('\n');
    /// <summary>A path shown on its own line under <see cref="Detail"/>.</summary>
    public string DetailPath { get; }
    public bool HasDetailPath => DetailPath != "";
    public string Button { get; }
    public string CancelText { get; }

    private void OnConfirm(object? sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close(true);
    }
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
