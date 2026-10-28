using Avalonia.Controls;
using Avalonia.Interactivity;

namespace BoosterAI;

public partial class CloseAllWindow : Window
{
    private readonly CloseAllOperation _operation;
    public CloseAllWindow() : this(new MainViewModel(new AppConfig()).PrepareCloseAll()) { }
    public CloseAllWindow(CloseAllOperation operation)
    {
        _operation = operation;
        DataContext = operation;
        InitializeComponent();
        Closing += (_, _) => operation.Cancel();
        operation.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CloseAllOperation.Active) && operation.Started && !operation.Active) Close();
        };
    }
    private void OnStart(object? sender, RoutedEventArgs e) => _operation.Start();
    private void OnForce(object? sender, RoutedEventArgs e) => _operation.Force();
    private void OnCancel(object? sender, RoutedEventArgs e) { _operation.Cancel(); Close(); }
}
