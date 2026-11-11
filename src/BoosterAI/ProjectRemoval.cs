namespace BoosterAI;

/// <summary>The exact group scope shown before a project is removed from configuration.</summary>
public sealed record ProjectRemovalConfirmation(string ProjectName, int SubTabCount)
{
    public string Heading => Loc.Format("project.remove.confirm.title", ProjectName);
    public string Body => Loc.Format("project.remove.confirm.body", SubTabCount);
}

public sealed partial class MainViewModel
{
    private IEnumerable<TabViewModel> ProjectGroup(TabViewModel project) =>
        Tabs.Where(t => t == project || t.Parent == project);

    public bool CanRemoveProject(TabViewModel project) => project.IsProject && Tabs.Contains(project)
        && ProjectGroup(project).All(t => !t.IsAlive && !t.HasOpenTerminal);

    /// <summary>Confirmation never closes sessions; recheck runtime state and the described group afterward.</summary>
    public async Task<bool> RemoveProjectAsync(TabViewModel project, Func<ProjectRemovalConfirmation, Task<bool>> confirm)
    {
        if (!CanRemoveProject(project)) return false;
        var group = ProjectGroup(project).ToArray();
        var request = new ProjectRemovalConfirmation(project.Name.Trim(), group.Length - 1);
        if (!await confirm(request)) return false;
        if (!group.SequenceEqual(ProjectGroup(project)))
        {
            ShowError(ErrorKind.ProjectRemoval, Loc.Get("project.remove.changed"));
            return false;
        }
        return RemoveProject(project, confirmed: true);
    }

    /// <summary>Call only after explicit confirmation. Unconfirmed and newly active groups stay configured.</summary>
    public bool RemoveProject(TabViewModel project, bool confirmed = false)
    {
        if (!confirmed) return false;
        if (!CanRemoveProject(project))
        {
            ShowError(ErrorKind.ProjectRemoval, Loc.Get("project.remove.blocked"));
            return false;
        }
        foreach (var tab in ProjectGroup(project).ToArray()) Tabs.Remove(tab);
        Save();
        return true;
    }
}
