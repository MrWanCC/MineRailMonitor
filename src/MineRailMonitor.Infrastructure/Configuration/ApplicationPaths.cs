using System;
using System.IO;

namespace MineRailMonitor.Infrastructure.Configuration;

public sealed class ApplicationPaths
{
    public ApplicationPaths(string baseDirectory, string? explicitRootDirectory)
    {
        var normalizedBase = NormalizeDirectory(baseDirectory, nameof(baseDirectory));
        var baseInfo = new DirectoryInfo(normalizedBase);

        var root = !string.IsNullOrWhiteSpace(explicitRootDirectory)
            ? NormalizeDirectory(explicitRootDirectory!, nameof(explicitRootDirectory))
            : string.Equals(baseInfo.Name, "App", StringComparison.OrdinalIgnoreCase)
                ? baseInfo.Parent?.FullName
                    ?? throw new ArgumentException(
                        "Installed App directory has no parent.",
                        nameof(baseDirectory))
                : normalizedBase;

        RootDirectory = NormalizeDirectory(root, nameof(explicitRootDirectory));
        AppDirectory = Path.Combine(RootDirectory, "App");
        ProjectsDirectory = Path.Combine(RootDirectory, "Projects");
        DataDirectory = Path.Combine(RootDirectory, "Data");
        DatabasePath = Path.Combine(DataDirectory, "MineRailMonitor.db");
        BackupsDirectory = Path.Combine(RootDirectory, "Backups");
        SqliteBackupDirectory = Path.Combine(BackupsDirectory, "SQLite");
        LogsDirectory = Path.Combine(RootDirectory, "Logs");
        BlackBoxDirectory = Path.Combine(LogsDirectory, "BlackBox");
        DocsDirectory = Path.Combine(RootDirectory, "Docs");
    }

    public string RootDirectory { get; }

    public string AppDirectory { get; }

    public string ProjectsDirectory { get; }

    public string DataDirectory { get; }

    public string DatabasePath { get; }

    public string BackupsDirectory { get; }

    public string SqliteBackupDirectory { get; }

    public string LogsDirectory { get; }

    public string BlackBoxDirectory { get; }

    public string DocsDirectory { get; }

    public void EnsureWritableDirectories()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(ProjectsDirectory);
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(SqliteBackupDirectory);
        Directory.CreateDirectory(BlackBoxDirectory);
        Directory.CreateDirectory(DocsDirectory);
    }

    private static string NormalizeDirectory(string directory, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("A directory is required.", parameterName);
        }

        var fullPath = Path.GetFullPath(directory);
        var pathRoot = Path.GetPathRoot(fullPath);

        if (string.Equals(fullPath, pathRoot, StringComparison.OrdinalIgnoreCase))
        {
            return fullPath;
        }

        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
