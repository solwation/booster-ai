using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace BoosterAI;

/// <summary>The questions behind a tab's question badge (#249); MainWindow shows it in a flyout below the badge.</summary>
public partial class QuestionPanelView : UserControl
{
    public QuestionPanelView() => InitializeComponent();

    /// <summary>The last question was answered or removed: the panel has nothing left to show.</summary>
    internal event Action? Emptied;

    private void OnOption(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: QuestionOption option }) option.Choose();
        CheckEmpty();
    }

    private void OnSend(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: QuestionViewModel question }) question.SendFreeText();
        CheckEmpty();
    }

    private void OnFreeTextKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox { DataContext: QuestionViewModel question }) return;
        e.Handled = true;
        question.SendFreeText();
        CheckEmpty();
    }

    private void OnDismiss(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: QuestionViewModel question }) question.Dismiss();
        CheckEmpty();
    }

    private void CheckEmpty()
    {
        if (DataContext is TabViewModel { HasQuestionBadge: false }) Emptied?.Invoke();
    }

    /// <summary>Keyboard focus goes to the first answer control (an option, the free text or Remove), else the panel.</summary>
    internal void FocusContent()
    {
        var first = this.GetVisualDescendants().OfType<Control>()
            .FirstOrDefault(c => c is Button or TextBox && c.IsEffectivelyVisible && c.IsEffectivelyEnabled);
        if (first is not null) first.Focus(NavigationMethod.Tab);
        else QuestionScroll.Focus(NavigationMethod.Tab);
    }
}
