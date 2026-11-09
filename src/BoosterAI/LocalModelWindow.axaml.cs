using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace BoosterAI;

public partial class LocalModelWindow : Window
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly LocalModelViewModel _vm;
    public LocalModelWindow() : this(new LocalModelViewModel("aider", Catalog.StandardModel)) { }
    internal LocalModelWindow(LocalModelViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
        Closed += (_, _) => _lifetime.Cancel();
    }
    private async void OnCheck(object? sender, RoutedEventArgs e)
    {
        if (!Screenshot.Active) await _vm.CheckAsync(_lifetime.Token);
    }
    private void OnSources(object? sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("https://github.com/solwation/booster-ai/blob/main/docs/DETAILS.md#local-model-hardware-advice") { UseShellExecute = true })?.Dispose();
    private void OnDone(object? sender, RoutedEventArgs e) => Close();
}
