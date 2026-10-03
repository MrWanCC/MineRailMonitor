using MineRailMonitor.Infrastructure.Configuration;
using MineRailMonitor.Infrastructure.Logging;

namespace MineRailMonitor.Infrastructure.Tests;

public sealed class ProjectDirectoryResolverTests
{
    [Fact]
    public async Task Production_loads_default_when_default_and_example_both_exist()
    {
        using var projects = TemporaryProjectsDirectory.Create(includeDefault: true, includeExample: true);
        var selected = ProjectDirectoryResolver.Resolve(projects.Path, acceptanceMode: false);
        var service = CreateService();

        var result = await service.LoadAsync(selected);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Errors));
        Assert.Equal("Default", result.Project!.Id);
    }

    [Fact]
    public void Production_does_not_fall_back_to_example_when_default_is_missing()
    {
        using var projects = TemporaryProjectsDirectory.Create(includeDefault: false, includeExample: true);

        var selected = ProjectDirectoryResolver.Resolve(projects.Path, acceptanceMode: false);

        Assert.Equal(Path.Combine(projects.Path, "Default"), selected);
        Assert.False(File.Exists(Path.Combine(selected, "project.json")));
        Assert.True(File.Exists(Path.Combine(projects.Path, "Example", "project.json")));
    }

    [Fact]
    public void Acceptance_explicitly_uses_example()
    {
        using var projects = TemporaryProjectsDirectory.Create(includeDefault: true, includeExample: true);

        var selected = ProjectDirectoryResolver.Resolve(projects.Path, acceptanceMode: true);

        Assert.Equal(Path.Combine(projects.Path, "Example"), selected);
    }

    [Fact]
    public async Task Missing_default_fails_to_load_even_when_example_is_valid()
    {
        var projectsDirectory = Path.Combine(AppContext.BaseDirectory, "Projects");
        var selected = ProjectDirectoryResolver.Resolve(projectsDirectory, acceptanceMode: false);
        var example = ProjectDirectoryResolver.Resolve(projectsDirectory, acceptanceMode: true);
        var service = new ProjectConfigService(new FileLogger(Path.Combine(AppContext.BaseDirectory, "test-logs")));

        Assert.True(File.Exists(Path.Combine(example, "project.json")));

        var result = await service.LoadAsync(selected);

        Assert.False(result.Succeeded);
        Assert.Null(result.Project);
        Assert.Contains(result.Errors, error => error.IndexOf("未找到项目配置文件", StringComparison.Ordinal) >= 0);
    }

    [Fact]
    public async Task Corrupt_default_fails_to_load_instead_of_using_example()
    {
        using var projects = TemporaryProjectsDirectory.Create(includeDefault: true, includeExample: true);
        File.WriteAllText(Path.Combine(projects.Path, "Default", "project.json"), "{ invalid json");
        var selected = ProjectDirectoryResolver.Resolve(projects.Path, acceptanceMode: false);
        var result = await CreateService().LoadAsync(selected);

        Assert.False(result.Succeeded);
        Assert.Null(result.Project);
        Assert.Contains(result.Errors, error => error.IndexOf("JSON", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static ProjectConfigService CreateService() =>
        new(new FileLogger(Path.Combine(AppContext.BaseDirectory, "test-logs")));

    private sealed class TemporaryProjectsDirectory : IDisposable
    {
        private TemporaryProjectsDirectory(string path) => Path = path;

        public string Path { get; }

        public static TemporaryProjectsDirectory Create(bool includeDefault, bool includeExample)
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "MineRailMonitor-Projects-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            if (includeDefault)
            {
                CreateProject(path, "Default");
            }
            if (includeExample)
            {
                CreateProject(path, "Example");
            }
            return new TemporaryProjectsDirectory(path);
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);

        private static void CreateProject(string projectsDirectory, string projectName)
        {
            var directory = System.IO.Path.Combine(projectsDirectory, projectName);
            Directory.CreateDirectory(System.IO.Path.Combine(directory, "stations"));
            File.WriteAllText(System.IO.Path.Combine(directory, "stations", "560.json"), """
                {
                  "Id": "560",
                  "Name": "测试站场",
                  "BackgroundImage": "",
                  "CadMinX": 0,
                  "CadMaxX": 100,
                  "CadMinY": 0,
                  "CadMaxY": 100,
                  "Devices": []
                }
                """);
            Directory.CreateDirectory(directory);
            File.WriteAllText(System.IO.Path.Combine(directory, "project.json"), $$"""
                {
                  "Id": "{{projectName}}",
                  "Name": "{{projectName}}",
                  "DefaultStationId": "560",
                  "RfidStations": [],
                  "Vehicles": [],
                  "YardCommunications": [],
                  "Stations": [ { "Id": "560", "ConfigFile": "stations/560.json" } ]
                }
                """);
        }
    }
}
