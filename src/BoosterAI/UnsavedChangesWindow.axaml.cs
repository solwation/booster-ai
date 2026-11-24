using Avalonia.Controls;
using Avalonia.Interactivity;

namespace BoosterAI;

/// <summary>
/// Asks what to do with edits that Done or close would otherwise lose (#188): Save, Discard or Cancel (the default).
/// A draft that cannot be saved (invalid) is listed, Save then only writes the others.
/// </summary>
public partial class UnsavedChangesWindow : Window
{
    // Required by the XAML previewer.
    public UnsavedChangesWindow() : this([]) { }

    public UnsavedChangesWindow(IReadOnlyList<UnsavedItem> items)
    {
        Items = items;
        DataContext = this;
        InitializeComponent();
    }

    public IReadOnlyList<UnsavedItem> Items { get; }
    public bool CanSaveAny => Items.Any(i => i.CanSave);
    public bool HasInvalid => Items.Any(i => !i.CanSave);

    /// <summary>Shows the question over <paramref name="owner"/>; closing the window any other way counts as Cancel.</summary>
    public static async Task<UnsavedChoice> AskAsync(Window owner, IReadOnlyList<UnsavedItem> items) =>
        await new UnsavedChangesWindow(items).ShowDialog<UnsavedChoice?>(owner) ?? UnsavedChoice.Cancel;

    private void OnSave(object? sender, RoutedEventArgs e) => Close(UnsavedChoice.Save);
    private void OnDiscard(object? sender, RoutedEventArgs e) => Close(UnsavedChoice.Discard);
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(UnsavedChoice.Cancel);
}
