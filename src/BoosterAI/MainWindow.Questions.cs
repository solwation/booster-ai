using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace BoosterAI;

/// <summary>The question badge's panel (#249), opened by a click on the badge, Enter/Space on it, or a notification's click.</summary>
public partial class MainWindow
{
    private Flyout? _questionFlyout;
    private Control? _questionTarget;
    /// <summary>The open question panel; the screenshot draws it.</summary>
    internal QuestionPanelView? QuestionPanel { get; private set; }

    private void OnQuestionBadge(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: TabViewModel tab } target) return;
        if (_questionTarget == target && _questionFlyout?.IsOpen == true) { CloseQuestions(restoreFocus: true); return; }
        ShowQuestions(target, tab);
    }

    /// <summary>A notification's click: the questions of the tab that asked last (MainViewModel.QuestionTarget), if any is open.</summary>
    public void OpenLatestQuestion()
    {
        if (_vm.QuestionTarget() is not { } tab) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (QuestionBadge(tab) is not { } target) return;
            target.BringIntoView();
            ShowQuestions(target, tab);
        }, DispatcherPriority.Loaded);
    }

    internal Button? QuestionBadge(TabViewModel tab) =>
        this.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Classes.Contains("question-badge") && b.DataContext == tab && b.IsEffectivelyVisible);

    internal void ShowQuestions(Control target, TabViewModel tab)
    {
        CloseQuestions(restoreFocus: false);
        var content = new QuestionPanelView { DataContext = tab, MaxHeight = Math.Max(140, ClientSize.Height - 48) };
        var flyout = new Flyout { Content = content, Placement = PlacementMode.Bottom };
        flyout.FlyoutPresenterClasses.Add("status-output");
        _questionFlyout = flyout;
        _questionTarget = target;
        QuestionPanel = content;
        content.Emptied += () => CloseQuestions(restoreFocus: true);
        content.AddHandler(KeyDownEvent, (_, key) =>
        {
            if (key.Key != Key.Escape) return;
            key.Handled = true;
            CloseQuestions(restoreFocus: true);
        }, RoutingStrategies.Tunnel);
        flyout.Opened += (_, _) => content.FocusContent();
        flyout.Closed += (_, _) =>
        {
            if (_questionFlyout != flyout) return;
            _questionFlyout = null;
            _questionTarget = null;
            QuestionPanel = null;
        };
        flyout.ShowAt(target);
    }

    private void CloseQuestions(bool restoreFocus)
    {
        var target = _questionTarget;
        _questionFlyout?.Hide();
        if (!restoreFocus || target is null) return;
        // The badge is gone once its last question is answered: focus the row's status instead.
        if (target.IsEffectivelyVisible) target.Focus(NavigationMethod.Tab);
        else if (target.DataContext is TabViewModel tab
            && this.GetVisualDescendants().OfType<StatusPeekButton>().FirstOrDefault(b => b.DataContext == tab && b.IsEffectivelyVisible) is { } status)
            status.Focus(NavigationMethod.Tab);
    }
}
