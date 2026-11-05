using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;

namespace BoosterAI;

/// <summary>One offline, theme-aware mark shared by every agent selector and settings page.</summary>
public sealed class AgentIcon : Image
{
    public static readonly StyledProperty<string> CliProperty = AvaloniaProperty.Register<AgentIcon, string>(nameof(Cli), Catalog.ShellOnly);
    public string Cli { get => GetValue(CliProperty); set => SetValue(CliProperty, value); }
    private static readonly Dictionary<string, Bitmap> Images = [];
    public AgentIcon() => ActualThemeVariantChanged += (_, _) => Refresh();
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CliProperty) Refresh();
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); Refresh(); }
    private void Refresh()
    {
        var cli = Catalog.Llms.Contains(Cli) ? Cli : Catalog.ShellOnly;
        var key = cli + (ActualThemeVariant == ThemeVariant.Dark ? "-dark" : "-light");
        if (!Images.TryGetValue(key, out var bitmap))
        {
            using var stream = AssetLoader.Open(new Uri("avares://BoosterAI/Assets/Agents/" + key + ".png"));
            Images[key] = bitmap = new Bitmap(stream);
        }
        Source = bitmap;
    }
}
