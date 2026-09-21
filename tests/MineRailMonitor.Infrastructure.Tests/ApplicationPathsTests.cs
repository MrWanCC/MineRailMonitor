using System;
using System.IO;
using MineRailMonitor.Infrastructure.Configuration;

namespace MineRailMonitor.Infrastructure.Tests;

public sealed class ApplicationPathsTests
{
    [Fact]
    public void Installed_layout_uses_parent_of_App_directory()
    {
        var paths = new ApplicationPaths(
            @"D:\MineRailMonitor\App\",
            explicitRootDirectory: null);

        Assert.Equal(
            Path.GetFullPath(@"D:\MineRailMonitor"),
            paths.RootDirectory,
            ignoreCase: true);
    }

    [Fact]
    public void Installed_layout_App_name_is_case_insensitive()
    {
        var paths = new ApplicationPaths(
            @"C:\Deploy\aPp\",
            explicitRootDirectory: null);

        Assert.Equal(Path.GetFullPath(@"C:\Deploy"), paths.RootDirectory, ignoreCase: true);
    }

    [Fact]
    public void Development_layout_uses_base_directory_as_root()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "MineRailMonitor", "bin", "Debug", "net48");

        var paths = new ApplicationPaths(baseDirectory, explicitRootDirectory: null);

        Assert.Equal(Path.GetFullPath(baseDirectory), paths.RootDirectory, ignoreCase: true);
    }

    [Fact]
    public void Development_output_named_App_is_indistinguishable_from_installed_layout()
    {
        var paths = new ApplicationPaths(@"C:\Build\App\", explicitRootDirectory: null);

        Assert.Equal(Path.GetFullPath(@"C:\Build"), paths.RootDirectory, ignoreCase: true);
    }

    [Fact]
    public void Explicit_root_override_wins_over_base_directory()
    {
        var paths = new ApplicationPaths(
            @"C:\Build\bin\Debug\net48\",
            @"D:\Field\MineRailMonitor\");

        Assert.Equal(Path.GetFullPath(@"D:\Field\MineRailMonitor"), paths.RootDirectory, ignoreCase: true);
    }

    [Fact]
    public void Derived_directories_are_under_application_root()
    {
        var paths = new ApplicationPaths(@"C:\Field\MineRailMonitor\App\", null);

        Assert.Equal(Path.Combine(paths.RootDirectory, "App"), paths.AppDirectory);
        Assert.Equal(Path.Combine(paths.RootDirectory, "Projects"), paths.ProjectsDirectory);
        Assert.Equal(Path.Combine(paths.RootDirectory, "Data"), paths.DataDirectory);
        Assert.Equal(Path.Combine(paths.RootDirectory, "Backups"), paths.BackupsDirectory);
        Assert.Equal(Path.Combine(paths.RootDirectory, "Logs"), paths.LogsDirectory);
        Assert.Equal(Path.Combine(paths.RootDirectory, "Docs"), paths.DocsDirectory);
        Assert.Equal(Path.Combine(paths.LogsDirectory, "BlackBox"), paths.BlackBoxDirectory);
        Assert.Equal(Path.Combine(paths.BackupsDirectory, "SQLite"), paths.SqliteBackupDirectory);
    }

    [Fact]
    public void Database_path_is_Root_Data_MineRailMonitor_db()
    {
        var paths = new ApplicationPaths(@"C:\Field\MineRailMonitor\", null);

        Assert.Equal(
            Path.Combine(paths.RootDirectory, "Data", "MineRailMonitor.db"),
            paths.DatabasePath);
    }

    [Fact]
    public void Paths_are_normalized_without_manual_dot_dot_segments()
    {
        var paths = new ApplicationPaths(
            @"C:\Build\bin\Debug\net48\",
            @".\artifacts\..\field\MineRailMonitor\");

        Assert.Equal(
            Path.GetFullPath(@"field\MineRailMonitor"),
            paths.RootDirectory,
            ignoreCase: true);
        Assert.DoesNotContain("..", paths.RootDirectory, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureWritableDirectories_creates_only_runtime_data_directories()
    {
        var root = Path.Combine(Path.GetTempPath(), "MineRailMonitor", Guid.NewGuid().ToString("N"));
        var paths = new ApplicationPaths(root, root);

        try
        {
            paths.EnsureWritableDirectories();

            Assert.True(Directory.Exists(paths.RootDirectory));
            Assert.True(Directory.Exists(paths.ProjectsDirectory));
            Assert.True(Directory.Exists(paths.DataDirectory));
            Assert.True(Directory.Exists(paths.SqliteBackupDirectory));
            Assert.True(Directory.Exists(paths.BlackBoxDirectory));
            Assert.True(Directory.Exists(paths.DocsDirectory));
            Assert.False(Directory.Exists(paths.AppDirectory));
            Assert.False(File.Exists(Path.Combine(paths.AppDirectory, "MineRailMonitor.exe")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
