namespace MineRailMonitor.Core.Tests;

public sealed class ProjectDirectorySelectionTests
{
    [Fact]
    public void Production_startup_selects_only_default_while_acceptance_is_explicit()
    {
        var code = File.ReadAllText(Locate("src", "MineRailMonitor", "MainWindow.xaml.cs"));

        Assert.Contains("ProjectDirectoryResolver.Resolve", code, StringComparison.Ordinal);
        Assert.Contains("_acceptanceOptions.Enabled", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveProjectDirectory", code, StringComparison.Ordinal);
        Assert.DoesNotContain(": Path.Combine(projectsDirectory, \"Example\")", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Project_load_failure_returns_before_communication_manager_is_created()
    {
        var code = File.ReadAllText(Locate("src", "MineRailMonitor", "MainWindow.xaml.cs"));
        var loadProject = ExtractMethod(code, "private async Task LoadProjectAsync()");
        var failureGuard = loadProject.IndexOf("if (!result.Succeeded || result.Project is null)", StringComparison.Ordinal);
        var earlyReturn = loadProject.IndexOf("return;", failureGuard, StringComparison.Ordinal);
        var managerCreation = loadProject.IndexOf("RecreateYardCommunicationManagerAsync", StringComparison.Ordinal);

        Assert.True(failureGuard >= 0);
        Assert.True(earlyReturn > failureGuard);
        Assert.True(managerCreation > earlyReturn);
    }

    [Fact]
    public void Missing_production_project_shows_setup_instructions()
    {
        var code = File.ReadAllText(Locate("src", "MineRailMonitor", "MainWindow.xaml.cs"));

        Assert.Contains("现场项目配置缺失", code, StringComparison.Ordinal);
        Assert.Contains("请配置 Projects/Default/project.json 后重新启动", code, StringComparison.Ordinal);
    }

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find method: {signature}");
        var nextMethod = source.IndexOf("\n    private ", start + signature.Length, StringComparison.Ordinal);
        return nextMethod < 0
            ? source.Substring(start)
            : source.Substring(start, nextMethod - start);
    }

    private static string Locate(params string[] parts)
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            var candidate = Path.Combine(new[] { directory }.Concat(parts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }
            directory = Directory.GetParent(directory)?.FullName ?? string.Empty;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar.ToString(), parts));
    }
}
