using System;
using System.IO;

namespace MineRailMonitor.Infrastructure.Configuration;

public static class ProjectDirectoryResolver
{
    public static string Resolve(string projectsDirectory, bool acceptanceMode)
    {
        if (string.IsNullOrWhiteSpace(projectsDirectory))
        {
            throw new ArgumentException("Projects directory is required.", nameof(projectsDirectory));
        }

        return Path.Combine(projectsDirectory, acceptanceMode ? "Example" : "Default");
    }
}
