using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace BoosterAI;

public partial class SettingsModelAdviceView : UserControl
{
    private CancellationTokenSource? _request;
    public SettingsModelAdviceView()
    {
        InitializeComponent();
        DetachedFromVisualTree += (_, _) => _request?.Cancel();
    }
    private async void OnCheckExact(object? sender, RoutedEventArgs e)
    {
        if (Screenshot.Active || DataContext is not SettingsModelAdviceViewModel vm) return;
        _request?.Dispose();
        _request = new();
        await vm.Exact.CheckAsync(_request.Token, vm.MeasuredHardware);
    }
    private void OnProfileSource(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsModelAdviceViewModel { HasProfile: true } vm) return;
        try { Process.Start(new ProcessStartInfo(vm.Source) { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { }
    }
}
