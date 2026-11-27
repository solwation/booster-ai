using Avalonia.Controls;
using Avalonia.Interactivity;

namespace BoosterAI;

/// <summary>The row menu's Arguments… (#260): edits the start command's arguments as one line; closes with the new text, or null on cancel.</summary>
public partial class CommandArgsWindow : Window
{
    public CommandArgsWindow() : this("/command", "") { }

    public CommandArgsWindow(string command, string args)
    {
        InitializeComponent();
        Heading.Text = Loc.Format("command.args.title", command);
        ArgsBox.Text = StartCommands.OneLine(args);
        Opened += (_, _) => { ArgsBox.Focus(); ArgsBox.SelectAll(); };
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
    private void OnSave(object? sender, RoutedEventArgs e) => Close(StartCommands.OneLine(ArgsBox.Text));
}
