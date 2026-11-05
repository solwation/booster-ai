using Avalonia.Controls;
using Avalonia.Input;

namespace BoosterAI;

/// <summary>Consumes activation keys before the window's default launch action can handle them.</summary>
public sealed class StatusPeekButton : Button
{
    protected override Type StyleKeyOverride => typeof(Button);
    private Key? _pressed;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Space)
        {
            e.Handled = true;
            if (_pressed is null) { _pressed = e.Key; OnClick(); }
            return;
        }
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Space)
        {
            _pressed = null;
            e.Handled = true;
            return;
        }
        base.OnKeyUp(e);
    }

    protected override void OnLostFocus(Avalonia.Interactivity.RoutedEventArgs e)
    {
        _pressed = null;
        base.OnLostFocus(e);
    }
}
