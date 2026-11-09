using Avalonia.Controls;

namespace BoosterAI;

public partial class SettingsWindow
{
    private CancellationTokenSource? _hardwareAdviceLifetime;
    private void ConfigureHardwareAdvice()
    {
        AgentsTab.PropertyChanged += (_, e) =>
        {
            if (e.Property == TabItem.IsSelectedProperty && AgentsTab.IsSelected) ReadHardwareAdvice();
        };
        Opened += (_, _) => { if (AgentsTab.IsSelected) ReadHardwareAdvice(); };
        Closed += (_, _) => _hardwareAdviceLifetime?.Cancel();
    }
    private async void ReadHardwareAdvice()
    {
        if (!IsVisible || !_vm.CanReadHardwareAdvice) return;
        _hardwareAdviceLifetime ??= new();
        await _vm.Setup.HardwareAdvice.EnsureAsync(_hardwareAdviceLifetime.Token);
    }
}

public sealed partial class MainViewModel
{
    // StartSetupChecks is the real-app opt-in. Constructors, previews, tests and screenshots never opt in.
    internal bool CanReadHardwareAdvice => _setupEnabled && !Screenshot.Active;
}
