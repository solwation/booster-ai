using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace BoosterAI;

public partial class LocalModelsWindow : Window
{
    private readonly CancellationTokenSource _lifetime = new();
    internal LocalModelsViewModel ViewModel { get; }
    // Preview construction, tests and screenshots never opt in to live probes.
    public LocalModelsWindow() : this(new LocalModelsViewModel()) { }
    internal LocalModelsWindow(LocalModelsViewModel vm, bool liveReads = false)
    {
        ViewModel = vm;
        DataContext = vm;
        InitializeComponent();
        // Fill the viewport so the list's * row grows with the window; content taller than that scrolls instead.
        ModelScroll.SizeChanged += (_, e) => ScrollContent.MinHeight = Math.Max(0, e.NewSize.Height - ScrollContent.Margin.Bottom);
        Opened += (_, _) => { DoneButton.Focus(); ModelScroll.Offset = default; };
        Opened += async (_, _) => { if (liveReads && !Screenshot.Active) await vm.RefreshAsync(_lifetime.Token); };
        Closed += (_, _) => { _lifetime.Cancel(); vm.Dispose(); _lifetime.Dispose(); };
    }
    private async void OnRefresh(object? sender, RoutedEventArgs e)
    { if (!Screenshot.Active) await ViewModel.RefreshAsync(_lifetime.Token); }
    private async void OnDownload(object? sender, RoutedEventArgs e)
    { if (!Screenshot.Active) await ViewModel.DownloadAsync(_lifetime.Token); }
    private void OnCancel(object? sender, RoutedEventArgs e) => ViewModel.Cancel();
    private void OnDone(object? sender, RoutedEventArgs e) => Close();
    private void OnSource(object? sender, RoutedEventArgs e) => Open(ViewModel.Selected.Model.Source);
    private void OnInstallOllama(object? sender, RoutedEventArgs e) => Open("https://ollama.com/download");
    private void OnAgentDocs(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string cli }) return;
        Open(cli switch
        {
            "aider" => "https://aider.chat/docs/llms/ollama.html",
            "interpreter" => "https://www.openinterpreter.com/docs/terminal/models",
            _ => "https://docs.openhands.dev/openhands/usage/llms/local-llms",
        });
    }
    private static void Open(string url)
    {
        if (Screenshot.Active) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { }
    }
}
