using Avalonia.Controls;

namespace BoosterAI;

/// <summary>What <see cref="ProjectPickList"/> binds to: rows with a check box each, and the header check box for all of them.</summary>
public interface IProjectList
{
    System.Collections.ObjectModel.ObservableCollection<ImportItem> Items { get; }
    bool HasItems { get; }
    bool? AllSelected { get; set; }
}

/// <summary>The check-box project list shared by "Hitta projekt" and the setup wizard's project step (#159).</summary>
public partial class ProjectPickList : UserControl
{
    public ProjectPickList() => InitializeComponent();
}
