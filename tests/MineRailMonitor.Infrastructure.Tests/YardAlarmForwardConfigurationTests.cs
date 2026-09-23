using MineRailMonitor.Infrastructure.Configuration;
using MineRailMonitor.Infrastructure.Logging;

namespace MineRailMonitor.Infrastructure.Tests;

public sealed class YardAlarmForwardConfigurationTests
{
    [Fact]
    public async Task Loads_and_saves_independent_yard_alarm_forward_targets()
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
              "YardAlarmForwards": [
                { "YardId": "560", "Enabled": true, "TargetIp": "192.168.1.100", "TargetPort": 9001 },
                { "YardId": "620", "Enabled": false, "TargetIp": "", "TargetPort": 0 }
              ],
              "Stations": [
                { "Id": "560", "ConfigFile": "stations/560.json" },
                { "Id": "620", "ConfigFile": "stations/620.json" }
              ]
            }
            """);
        var service = CreateService();

        var loaded = await service.LoadAsync(project.Path);

        Assert.True(loaded.Succeeded, string.Join(Environment.NewLine, loaded.Errors));
        Assert.Equal(2, loaded.Project!.YardAlarmForwards.Count);
        Assert.Equal("192.168.1.100", loaded.Project.YardAlarmForwards.Single(item => item.YardId == "560").TargetIp);
        Assert.False(loaded.Project.YardAlarmForwards.Single(item => item.YardId == "620").Enabled);

        var updated = loaded.Project.YardAlarmForwards
            .Select(item => item.YardId == "620"
                ? new MineRailMonitor.Core.Models.YardAlarmForwardConfig
                {
                    YardId = item.YardId,
                    Enabled = true,
                    TargetIp = "192.168.1.200",
                    TargetPort = 9002
                }
                : item)
            .ToArray();
        var save = await service.SaveYardAlarmForwardsAsync(project.Path, updated);

        Assert.True(save.Succeeded, string.Join(Environment.NewLine, save.Errors));
        var reloaded = await service.LoadAsync(project.Path);
        var forward620 = reloaded.Project!.YardAlarmForwards.Single(item => item.YardId == "620");
        Assert.True(forward620.Enabled);
        Assert.Equal("192.168.1.200", forward620.TargetIp);
        Assert.Equal(9002, forward620.TargetPort);
    }

    [Fact]
    public async Task Missing_alarm_forward_field_loads_as_disabled_empty_configuration()
    {
        using var project = TemporaryProject.Create(
            """
            {
              "Id": "Temporary",
              "Name": "Temporary Project",
              "DefaultStationId": "560",
              "YardCommunications": [
                { "YardId": "560", "ListenIp": "127.0.0.1", "ListenPort": 62002, "Enabled": true }
              ],
              "Stations": [ { "Id": "560", "ConfigFile": "stations/560.json" } ]
            }
            """);

        var loaded = await CreateService().LoadAsync(project.Path);

        Assert.True(loaded.Succeeded, string.Join(Environment.NewLine, loaded.Errors));
        Assert.Empty(loaded.Project!.YardAlarmForwards);
    }

    [Fact]
    public async Task Legacy_project_cannot_enable_yard_alarm_forwarding()
    {
        using var project = TemporaryProject.Create(
            """
            {
              "Id": "Temporary",
              "Name": "Temporary Project",
              "DefaultStationId": "560",
              "YardAlarmForwards": [
                { "YardId": "560", "Enabled": true, "TargetIp": "127.0.0.1", "TargetPort": 9001 }
              ],
              "Stations": [ { "Id": "560", "ConfigFile": "stations/560.json" } ]
            }
            """);

        var loaded = await CreateService().LoadAsync(project.Path);

        Assert.False(loaded.Succeeded);
        Assert.Contains(loaded.Errors, error => error.IndexOf("迁移", StringComparison.Ordinal) >= 0);
    }

    private static ProjectConfigService CreateService() =>
        new(new FileLogger(Path.Combine(AppContext.BaseDirectory, "test-logs")));

    private sealed class TemporaryProject : IDisposable
    {
        private TemporaryProject(string path) => Path = path;

        public string Path { get; }

        public static TemporaryProject Create(string manifest)
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"MineRailMonitor-YardAlarmForward-{Guid.NewGuid():N}");
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
