using Avalonia.Controls;
using Avalonia.Interactivity;

namespace BoosterAI;

public partial class AutoCompactView : UserControl
{
    public AutoCompactView() => InitializeComponent();
    private void OnSave(object? sender, RoutedEventArgs e) => (DataContext as AutoCompactViewModel)?.Save();
}
