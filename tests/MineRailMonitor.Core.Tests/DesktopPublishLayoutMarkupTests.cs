namespace MineRailMonitor.Core.Tests;

public sealed class DesktopPublishLayoutMarkupTests
{
    [Fact]
    public void Projects_remain_in_development_output_but_are_excluded_from_publish()
    {
        var project = ReadSource("src", "MineRailMonitor", "MineRailMonitor.csproj");

        Assert.Contains("CopyToOutputDirectory=\"PreserveNewest\"", project, StringComparison.Ordinal);
        Assert.Contains("CopyToPublishDirectory=\"Never\"", project, StringComparison.Ordinal);
    }

    [Fact]
    public void Package_script_uses_only_the_Example_projects_whitelist()
    {
        var script = ReadSource("scripts", "build-desktop-package.ps1");

        Assert.Contains("dotnet publish", script, StringComparison.Ordinal);
        Assert.Contains("desktop-package", script, StringComparison.Ordinal);
        Assert.Contains("$exampleSource = Join-Path $repoRoot \"Projects\\Example\"", script, StringComparison.Ordinal);
        Assert.Contains("$exampleTarget = Join-Path $projectsDirectory \"Example\"", script, StringComparison.Ordinal);
        Assert.Contains("System.Data.SQLite.dll", script, StringComparison.Ordinal);
        Assert.Contains("x86\\SQLite.Interop.dll", script, StringComparison.Ordinal);
        Assert.Contains("x64\\SQLite.Interop.dll", script, StringComparison.Ordinal);
        Assert.Contains("project.json", script, StringComparison.Ordinal);
        Assert.Contains("throw", script, StringComparison.Ordinal);

        Assert.DoesNotContain("Join-Path $repoRoot \"Projects\\*\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Copy-Item (Join-Path $repoRoot \"Projects\\*\")", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Package_script_rejects_projects_nested_under_App()
    {
        var script = ReadSource("scripts", "build-desktop-package.ps1");

        Assert.Contains("$publishedProjects = Join-Path $appDirectory \"Projects\"", script, StringComparison.Ordinal);
        Assert.Contains("if (Test-Path $publishedProjects)", script, StringComparison.Ordinal);
        Assert.Contains("Publish output must not contain App\\Projects", script, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] segments) =>
        File.ReadAllText(Locate(segments));

    private static string Locate(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate source file: {Path.Combine(segments)}");
    }
}
