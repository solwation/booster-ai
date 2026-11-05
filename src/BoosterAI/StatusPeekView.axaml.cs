using Avalonia.Controls;

namespace BoosterAI;

/// <summary>Identical full status and live output for pointer preview and keyboard-opened details.</summary>
public partial class StatusPeekView : UserControl
{
    public StatusPeekView() => InitializeComponent();

    internal void FocusContent() => OutputScroll.Focus(Avalonia.Input.NavigationMethod.Tab);
}
