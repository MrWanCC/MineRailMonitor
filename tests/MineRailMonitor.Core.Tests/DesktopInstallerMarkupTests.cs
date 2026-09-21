namespace MineRailMonitor.Core.Tests;

public sealed class DesktopInstallerMarkupTests
{
    [Fact]
    public void Installer_uses_C_default_and_selected_directory_as_Root()
    {
        var script = ReadSource("installer", "MineRailMonitor.iss");

        Assert.Contains("DefaultDirName=C:\\MineRailMonitor", script, StringComparison.Ordinal);
        Assert.Contains("{app}\\App\\MineRailMonitor.exe", script, StringComparison.Ordinal);
        Assert.DoesNotContain("{app}\\MineRailMonitor", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Installer_deploys_App_and_root_level_Projects()
    {
        var script = ReadSource("installer", "MineRailMonitor.iss");

        Assert.Contains("desktop-package\\App", script, StringComparison.Ordinal);
        Assert.Contains("{app}\\App", script, StringComparison.Ordinal);
        Assert.Contains("desktop-package\\Projects", script, StringComparison.Ordinal);
        Assert.Contains("{app}\\Projects", script, StringComparison.Ordinal);
        Assert.Contains("onlyifdoesntexist", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Installer_preserves_existing_site_config_on_upgrade()
    {
        var script = ReadSource("installer", "MineRailMonitor.iss");

        Assert.Contains(
            "Source: \"..\\artifacts\\desktop-package\\App\\*\"; Excludes: \"MineRailMonitor.exe.config\";",
            script,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Source: \"..\\artifacts\\desktop-package\\App\\*\"; DestDir: \"{app}\\App\";",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "Source: \"..\\artifacts\\desktop-package\\App\\MineRailMonitor.exe.config\"",
            script,
            StringComparison.Ordinal);
        Assert.Contains("onlyifdoesntexist", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Installer_creates_only_the_documented_desktop_shortcut()
    {
        var script = ReadSource("installer", "MineRailMonitor.iss");

        Assert.Contains("矿车编组监控系统", script, StringComparison.Ordinal);
        Assert.Contains("{autodesktop}", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Service", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Registry Run", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Installer_uses_stable_AppId_and_checks_DotNet_Framework_48()
    {
        var script = ReadSource("installer", "MineRailMonitor.iss");

        Assert.Contains("AppId={{8C8B1CB5-4A4B-4B4A-9D48-6A3C93D2F0E1}", script, StringComparison.Ordinal);
        Assert.Contains("InitializeSetup", script, StringComparison.Ordinal);
        Assert.Contains("NET Framework Setup\\NDP\\v4\\Full", script, StringComparison.Ordinal);
        Assert.Contains("Release", script, StringComparison.Ordinal);
        Assert.Contains("528040", script, StringComparison.Ordinal);
        Assert.Contains("请先安装 .NET Framework 4.8", script, StringComparison.Ordinal);
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
