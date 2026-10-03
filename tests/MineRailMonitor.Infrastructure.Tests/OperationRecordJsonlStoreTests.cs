using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;
using MineRailMonitor.Infrastructure.Configuration;
using MineRailMonitor.Infrastructure.Logging;
using Newtonsoft.Json;

namespace MineRailMonitor.Infrastructure.Tests;

public sealed class OperationRecordJsonlStoreTests
{
    [Fact]
    public void Appends_records_as_jsonl_and_queries_newest_first()
    {
        var logsDirectory = Path.Combine(Path.GetTempPath(), "MineRailMonitorTests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new OperationRecordJsonlStore(logsDirectory);
            store.Append(CreateRecord("older", new DateTimeOffset(2026, 10, 1, 23, 59, 0, TimeSpan.Zero)));
            store.Append(CreateRecord("newer", new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.Zero)));

            var files = Directory.GetFiles(Path.Combine(logsDirectory, "Operation"), "operation-*.jsonl");
            Assert.Equal(2, files.Length);
            Assert.Equal(
                new[] { "operation-20261001.jsonl", "operation-20261002.jsonl" },
                files.Select(Path.GetFileName).OrderBy(item => item, StringComparer.Ordinal));
            Assert.All(files, path => Assert.Single(File.ReadAllLines(path)));
            Assert.Equal(new[] { "newer", "older" }, store.QueryPage(0, 10).Select(item => item.TargetName));
        }
        finally
        {
            if (Directory.Exists(logsDirectory)) Directory.Delete(logsDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Skips_malformed_lines_and_isolates_write_failure_from_project_configuration_save()
    {
        var root = Path.Combine(Path.GetTempPath(), "MineRailMonitorTests", Guid.NewGuid().ToString("N"));
        var projectDirectory = Path.Combine(root, "Project");
        var logsDirectory = Path.Combine(root, "Logs");
        Directory.CreateDirectory(projectDirectory);
        Directory.CreateDirectory(logsDirectory);
        File.WriteAllText(Path.Combine(projectDirectory, "project.json"), """
            { "Id": "test", "Name": "test", "RfidStations": [], "Stations": [] }
            """);
        File.WriteAllText(Path.Combine(logsDirectory, "Operation"), "not a directory");
        try
        {
            var service = new ProjectConfigService(new FileLogger(Path.Combine(root, "test-logs")));
            var saved = await service.SaveRfidSettingsAsync(projectDirectory, new RfidSettings
            {
                PollIntervalMs = 300,
                ExpectedVehicleCount = 11,
                InterVehicleTimeoutSeconds = 30
            });
            var store = new OperationRecordJsonlStore(logsDirectory);

            store.Append(CreateRecord("K001", new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.Zero)));

            Assert.True(saved.Succeeded, string.Join(Environment.NewLine, saved.Errors));
            Assert.Equal(300, (await service.LoadAsync(projectDirectory)).Project!.RfidSettings.PollIntervalMs);
            Assert.Empty(store.QueryPage(0, 10));
            Assert.Equal("not a directory", File.ReadAllText(Path.Combine(logsDirectory, "Operation")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Successful_vehicle_add_edit_and_delete_saves_create_operation_history()
    {
        var root = Path.Combine(Path.GetTempPath(), "MineRailMonitorTests", Guid.NewGuid().ToString("N"));
        var projectDirectory = Path.Combine(root, "Project");
        var logsDirectory = Path.Combine(root, "Logs");
        Directory.CreateDirectory(projectDirectory);
        File.WriteAllText(Path.Combine(projectDirectory, "project.json"), """
            { "Id": "test", "Name": "test", "RfidStations": [], "Stations": [] }
            """);
        try
        {
            var service = new ProjectConfigService(new FileLogger(Path.Combine(root, "test-logs")));
            var store = new OperationRecordJsonlStore(logsDirectory);
            IReadOnlyList<VehicleConfig> previous = Array.Empty<VehicleConfig>();
            var addedVehicles = new[] { CreateVehicle(2) };
            var addSave = await service.SaveVehiclesAsync(projectDirectory, addedVehicles);
            Assert.True(addSave.Succeeded, string.Join(Environment.NewLine, addSave.Errors));
            foreach (var operation in ConfigurationOperationRecordFormatter.GetVehicleChanges(previous, addedVehicles))
            {
                store.Append(operation);
            }

            var editedVehicles = new[] { CreateVehicle(3) };
            var editSave = await service.SaveVehiclesAsync(projectDirectory, editedVehicles);
            Assert.True(editSave.Succeeded, string.Join(Environment.NewLine, editSave.Errors));
            foreach (var operation in ConfigurationOperationRecordFormatter.GetVehicleChanges(addedVehicles, editedVehicles))
            {
                store.Append(operation);
            }

            var deleteSave = await service.SaveVehiclesAsync(projectDirectory, Array.Empty<VehicleConfig>());
            Assert.True(deleteSave.Succeeded, string.Join(Environment.NewLine, deleteSave.Errors));
            foreach (var operation in ConfigurationOperationRecordFormatter.GetVehicleChanges(editedVehicles, Array.Empty<VehicleConfig>()))
            {
                store.Append(operation);
            }

            var records = store.QueryPage(0, 10);
            Assert.Equal(3, records.Count);
            Assert.Contains(records, item => item.OperationType == "删除车辆配置");
            Assert.Contains(records, item => item.OperationType == "修改车辆配置");
            Assert.Contains(records, item => item.OperationType == "新增车辆配置");
            var byOperation = records.ToDictionary(item => item.OperationType, StringComparer.Ordinal);
            Assert.Contains("总节数: 2节", byOperation["新增车辆配置"].AfterValue, StringComparison.Ordinal);
            Assert.Contains("总节数: 2节", byOperation["修改车辆配置"].BeforeValue, StringComparison.Ordinal);
            Assert.Contains("总节数: 3节", byOperation["修改车辆配置"].AfterValue, StringComparison.Ordinal);
            Assert.Contains("总节数: 3节", byOperation["删除车辆配置"].BeforeValue, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Query_skips_malformed_lines_and_paginates_valid_records()
    {
        var logsDirectory = Path.Combine(Path.GetTempPath(), "MineRailMonitorTests", Guid.NewGuid().ToString("N"));
        var operationDirectory = Path.Combine(logsDirectory, "Operation");
        Directory.CreateDirectory(operationDirectory);
        try
        {
            File.WriteAllLines(Path.Combine(operationDirectory, "operation-20261002.jsonl"), new[]
            {
                "{invalid}",
                JsonConvert.SerializeObject(CreateRecord("first", new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.Zero))),
                JsonConvert.SerializeObject(CreateRecord("second", new DateTimeOffset(2026, 10, 2, 10, 1, 0, TimeSpan.Zero)))
            });
            var store = new OperationRecordJsonlStore(logsDirectory);

            Assert.Equal(new[] { "second" }, store.QueryPage(0, 1).Select(item => item.TargetName));
            Assert.Equal(new[] { "first" }, store.QueryPage(1, 1).Select(item => item.TargetName));
        }
        finally
        {
            if (Directory.Exists(logsDirectory)) Directory.Delete(logsDirectory, recursive: true);
        }
    }

    private static OperationRecord CreateRecord(string targetName, DateTimeOffset time) => new()
    {
        Id = Guid.NewGuid(),
        Time = time,
        OperatorName = "管理员",
        OperationType = "修改车辆配置",
        TargetType = "车辆",
        TargetName = targetName,
        BeforeValue = "总节数: 6节",
        AfterValue = "总节数: 7节",
        Result = "成功"
    };

    private static VehicleConfig CreateVehicle(int totalCount)
    {
        var tags = new List<VehicleTagConfig>
        {
            new() { Position = 0, Rfid = 1, Type = VehicleTagType.Head }
        };
        for (var position = 1; position < totalCount; position++)
        {
            tags.Add(new VehicleTagConfig
            {
                Position = position,
                Rfid = (ushort)(position + 0x10),
                Type = VehicleTagType.Carriage
            });
        }

        return new VehicleConfig
        {
            VehicleNo = "K001",
            HeadRfid = 1,
            VehicleName = "1号矿车",
            Tags = tags
        };
    }
}
