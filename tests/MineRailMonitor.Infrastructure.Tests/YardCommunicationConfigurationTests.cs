using MineRailMonitor.Infrastructure.Configuration;
using MineRailMonitor.Infrastructure.Logging;

namespace MineRailMonitor.Infrastructure.Tests;

public sealed class YardCommunicationConfigurationTests
{
    [Fact]
    public async Task Loads_and_saves_yard_communication_endpoints()
    {
        using var project = TemporaryProject.Create(
            """
            {
              "Id": "Temporary",
              "Name": "Temporary Project",
              "DefaultStationId": "560",
              "YardCommunications": [
                { "YardId": "560", "ListenIp": "127.0.0.1", "ListenPort": 62002, "Enabled": true },
                { "YardId": "620", "ListenIp": "127.0.0.1", "ListenPort": 62012, "Enabled": true }
              ],
              "Stations": [
                { "Id": "560", "ConfigFile": "stations/560.json" },
                { "Id": "620", "ConfigFile": "stations/620.json" }
              ]
            }
            """);
        var service = CreateService();

        var loaded = await service.LoadAsync(project.Path);

        Assert.False(loaded.Project!.UsesLegacySharedListener);
        Assert.Equal(2, loaded.Project.YardCommunications.Count);
        Assert.Equal(62012, loaded.Project.YardCommunications.Single(item => item.YardId == "620").ListenPort);

        var updated = loaded.Project.YardCommunications
            .Select(item =>
            {
                item.ListenPort = item.YardId == "620" ? 62022 : item.ListenPort;
                return item;
            })
            .ToArray();
        var save = await service.SaveYardCommunicationsAsync(project.Path, updated);

        Assert.True(save.Succeeded, string.Join(Environment.NewLine, save.Errors));
        var reloaded = await service.LoadAsync(project.Path);
        Assert.Equal(62022, reloaded.Project!.YardCommunications.Single(item => item.YardId == "620").ListenPort);
    }

    [Fact]
    public async Task Marks_old_manifest_without_yard_communication_configuration_as_legacy()
    {
        using var project = TemporaryProject.Create(
            """
            {
              "Id": "Temporary",
              "Name": "Temporary Project",
              "DefaultStationId": "560",
              "Stations": [
                { "Id": "560", "ConfigFile": "stations/560.json" },
                { "Id": "620", "ConfigFile": "stations/620.json" }
              ]
            }
            """);
        var service = CreateService();

        var loaded = await service.LoadAsync(project.Path);

        Assert.True(loaded.Project!.UsesLegacySharedListener);
        Assert.Empty(loaded.Project.YardCommunications);
    }

    [Fact]
    public async Task Rejects_duplicate_enabled_yard_listener_endpoints()
    {
        using var project = TemporaryProject.Create(
            """
            {
              "Id": "Temporary",
              "Name": "Temporary Project",
              "DefaultStationId": "560",
              "YardCommunications": [
                { "YardId": "560", "ListenIp": "127.0.0.1", "ListenPort": 62002, "Enabled": true },
                { "YardId": "620", "ListenIp": "127.0.0.1", "ListenPort": 62012, "Enabled": true }
              ],
              "Stations": [
                { "Id": "560", "ConfigFile": "stations/560.json" },
                { "Id": "620", "ConfigFile": "stations/620.json" }
              ]
            }
            """);
        var service = CreateService();
        var loaded = await service.LoadAsync(project.Path);

        loaded.Project!.YardCommunications.Single(item => item.YardId == "620").ListenPort = 62002;

        var save = await service.SaveYardCommunicationsAsync(project.Path, loaded.Project.YardCommunications);

        Assert.False(save.Succeeded);
        Assert.Contains(save.Errors, error => error.IndexOf("监听端点重复", StringComparison.Ordinal) >= 0);
    }

    [Fact]
    public async Task Rejects_duplicate_enabled_yard_listener_endpoints_when_loading_project()
    {
        using var project = TemporaryProject.Create(
            """
            {
              "Id": "Temporary",
              "Name": "Temporary Project",
              "DefaultStationId": "560",
              "YardCommunications": [
                { "YardId": "560", "ListenIp": "127.0.0.1", "ListenPort": 62002, "Enabled": true },
                { "YardId": "620", "ListenIp": "127.0.0.1", "ListenPort": 62002, "Enabled": true }
              ],
              "Stations": [
                { "Id": "560", "ConfigFile": "stations/560.json" },
                { "Id": "620", "ConfigFile": "stations/620.json" }
              ]
            }
            """);
        var service = CreateService();

        var loaded = await service.LoadAsync(project.Path);

        Assert.False(loaded.Succeeded);
        Assert.Contains(loaded.Errors, error => error.IndexOf("监听端点重复", StringComparison.Ordinal) >= 0);
    }

    private static ProjectConfigService CreateService() =>
        new(new FileLogger(Path.Combine(AppContext.BaseDirectory, "test-logs")));

    private sealed class TemporaryProject : IDisposable
    {
        private TemporaryProject(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryProject Create(string manifest)
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"MineRailMonitor-YardCommunication-{Guid.NewGuid():N}");
            Directory.CreateDirectory(System.IO.Path.Combine(path, "stations"));
            File.WriteAllText(System.IO.Path.Combine(path, "project.json"), manifest);
            File.WriteAllText(
                System.IO.Path.Combine(path, "stations", "560.json"),
                "{ \"Id\": \"560\", \"Name\": \"-560\", \"Devices\": [] }");
            File.WriteAllText(
                System.IO.Path.Combine(path, "stations", "620.json"),
                "{ \"Id\": \"620\", \"Name\": \"-620\", \"Devices\": [] }");
            return new TemporaryProject(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
