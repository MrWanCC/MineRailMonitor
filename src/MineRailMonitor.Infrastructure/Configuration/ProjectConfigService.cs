using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Services;
using MineRailMonitor.Infrastructure.Logging;
using Newtonsoft.Json.Linq;

namespace MineRailMonitor.Infrastructure.Configuration;

public sealed class ProjectConfigService : IProjectConfigService
{
    private readonly ILogger _logger;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ProjectConfigService(ILogger logger)
    {
        _logger = logger;
        _jsonOptions.Converters.Add(new JsonStringEnumConverter());
        _jsonOptions.Converters.Add(new ByteArrayJsonConverter());
    }

    public async Task<ProjectConfigLoadResult> LoadAsync(
        string projectDirectory,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            return Failure(errors, "项目目录不能为空。");
        }

        var manifestPath = Path.Combine(projectDirectory, "project.json");
        if (!File.Exists(manifestPath))
        {
            return Failure(errors, $"未找到项目配置文件：{manifestPath}");
        }

        ProjectManifest? manifest;
        try
        {
            using var reader = File.OpenText(manifestPath);
            var json = await reader.ReadToEndAsync();
            cancellationToken.ThrowIfCancellationRequested();
            json = NormalizeLegacyRfidSettings(json);
            manifest = JsonSerializer.Deserialize<ProjectManifest>(json, _jsonOptions);
        }
        catch (Newtonsoft.Json.JsonException exception)
        {
            return Failure(errors, $"项目配置 JSON 无效：{exception.Message}", exception);
        }
        catch (JsonException exception)
        {
            return Failure(errors, $"项目配置 JSON 无效：{exception.Message}", exception);
        }
        catch (IOException exception)
        {
            return Failure(errors, $"读取项目配置失败：{exception.Message}", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            return Failure(errors, $"无权读取项目配置：{exception.Message}", exception);
        }

        if (manifest is null)
        {
            return Failure(errors, "项目配置为空。");
        }

        var rfidStations = (manifest.RfidStations ?? Array.Empty<ProjectConfigService.ProjectManifest.RfidStationManifest>())
            .Select(item => item.ToConfig())
            .ToArray();
        ValidateUniqueRfidStationIds(rfidStations, errors);
        if (errors.Count > 0)
        {
            return ProjectConfigLoadResult.Failure(errors);
        }

        var stations = new List<StationConfig>();
        foreach (var stationReference in manifest.Stations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stationPath = Path.Combine(projectDirectory, stationReference.ConfigFile);
            if (!File.Exists(stationPath))
            {
                AddError(errors, $"未找到站场配置文件：{stationPath}");
                continue;
            }

            try
            {
                using var reader = File.OpenText(stationPath);
                var json = await reader.ReadToEndAsync();
                cancellationToken.ThrowIfCancellationRequested();
                var station = JsonSerializer.Deserialize<StationConfig>(json, _jsonOptions);
                if (station is null)
                {
                    AddError(errors, $"站场配置为空：{stationPath}");
                    continue;
                }

                stations.Add(station);
            }
            catch (JsonException exception)
            {
                AddError(errors, $"站场配置 JSON 无效：{stationPath}，{exception.Message}", exception);
            }
            catch (IOException exception)
            {
                AddError(errors, $"读取站场配置失败：{stationPath}，{exception.Message}", exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                AddError(errors, $"无权读取站场配置：{stationPath}，{exception.Message}", exception);
            }
        }

        if (errors.Count > 0)
        {
            return ProjectConfigLoadResult.Failure(errors);
        }

        var yardCommunications = (manifest.YardCommunications ?? Array.Empty<ProjectConfigService.ProjectManifest.YardCommunicationManifest>())
            .Select(item => item.ToConfig())
            .ToArray();
        ValidateYardCommunications(stations, yardCommunications, errors);
        if (errors.Count > 0)
        {
            return ProjectConfigLoadResult.Failure(errors);
        }

        var yardAlarmForwards = (manifest.YardAlarmForwards ?? Array.Empty<ProjectConfigService.ProjectManifest.YardAlarmForwardManifest>())
            .Select(item => item.ToConfig())
            .ToArray();
        ValidateYardAlarmForwards(
            stations,
            yardAlarmForwards,
            manifest.YardCommunications is null,
            errors);
        if (errors.Count > 0)
        {
            return ProjectConfigLoadResult.Failure(errors);
        }

        var project = new ProjectConfig
        {
            Id = manifest.Id,
            Name = manifest.Name,
            DefaultStationId = manifest.DefaultStationId,
            Stations = stations,
            RfidSettings = manifest.RfidSettings ?? new RfidSettings(),
            RfidStations = rfidStations,
            YardCommunications = yardCommunications,
            YardAlarmForwards = yardAlarmForwards,
            UsesLegacySharedListener = manifest.YardCommunications is null
        };
        // Legacy map entries may still carry only ProtocolAddress. Resolve them in memory
        // for this process; the station JSON is changed only by an explicit map save.
        RfidMapBindingResolver.ApplyLegacyProtocolBindings(project.Stations, project.RfidStations);
        _logger.Information($"项目配置加载成功：{project.Name}，站场数：{project.Stations.Count}");
        return ProjectConfigLoadResult.Success(project);
    }

    public async Task<ProjectConfigSaveResult> SaveStationAsync(
        string projectDirectory,
        StationConfig station,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            return SaveFailure(errors, "项目目录不能为空。");
        }

        if (station is null || string.IsNullOrWhiteSpace(station.Id))
        {
            return SaveFailure(errors, "站场配置或站场编号不能为空。");
        }

        string? stationPath;
        try
        {
            stationPath = await ResolveStationPathAsync(projectDirectory, station.Id, cancellationToken);
        }
        catch (JsonException exception)
        {
            return SaveFailure(errors, $"项目配置 JSON 无效：{exception.Message}", exception);
        }
        catch (IOException exception)
        {
            return SaveFailure(errors, $"读取项目配置失败：{exception.Message}", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            return SaveFailure(errors, $"无权读取项目配置：{exception.Message}", exception);
        }

        if (string.IsNullOrWhiteSpace(stationPath))
        {
            return SaveFailure(errors, $"项目配置中未找到站场：{station.Id}");
        }

        var currentProject = await LoadAsync(projectDirectory, cancellationToken);
        if (!currentProject.Succeeded || currentProject.Project is null)
        {
            return SaveFailure(
                errors,
                currentProject.Errors.Count == 0
                    ? "读取项目配置失败，无法校验 RFID 地图绑定。"
                    : string.Join(Environment.NewLine, currentProject.Errors));
        }

        var existingBindingKeys = RfidStationBindingRules
            .FindDuplicateBindings(currentProject.Project.Stations)
            .Select(GetBindingKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidateYards = currentProject.Project.Stations
            .Select(item => string.Equals(item.Id, station.Id, StringComparison.OrdinalIgnoreCase) ? station : item)
            .ToArray();
        var newlyCreatedDuplicateBindings = RfidStationBindingRules
            .FindDuplicateBindings(candidateYards)
            .Where(binding => !existingBindingKeys.Contains(GetBindingKey(binding)))
            .ToArray();
        if (newlyCreatedDuplicateBindings.Length > 0)
        {
            return SaveFailure(
                errors,
                "保存被阻止：不能新增重复绑定 RFID 地图绑定。" + Environment.NewLine +
                string.Join(
                    Environment.NewLine,
                    newlyCreatedDuplicateBindings.Select(binding =>
                        $"• RFID 基站 {binding.RfidStationId}：{binding.DisplayName}")));
        }

        foreach (var device in (station.Devices ?? Array.Empty<DeviceConfig>()).Where(item =>
                     item is not null &&
                     item.Type == DeviceType.RfidStation &&
                     !string.IsNullOrWhiteSpace(item.RfidStationId)))
        {
            var bindingValidation = RfidStationBindingRules.ValidateBinding(
                candidateYards,
                currentProject.Project.RfidStations,
                station.Id,
                device.Id,
                device.RfidStationId);
            if (bindingValidation.Succeeded)
            {
                continue;
            }

            // Preserve legacy duplicate data when this save did not create it;
            // a newly introduced cross-yard ownership violation is always rejected.
            var introducesNewConflict = bindingValidation.Conflicts.Count == 0 ||
                bindingValidation.Conflicts.Any(conflict => !existingBindingKeys.Contains(GetBindingKey(conflict)));
            if (introducesNewConflict)
            {
                AddError(errors, bindingValidation.Message);
            }
        }
        if (errors.Count > 0)
        {
            return ProjectConfigSaveResult.Failure(errors);
        }

        if (!File.Exists(stationPath))
        {
            return SaveFailure(errors, $"未找到站场配置文件：{stationPath}");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = JObject.Parse(File.ReadAllText(stationPath));
            SetProperty(root, "Id", station.Id);
            SetProperty(root, "Name", station.Name);
            SetProperty(root, "BackgroundImage", station.BackgroundImage);
            SetProperty(root, "CadMinX", station.CadMinX);
            SetProperty(root, "CadMaxX", station.CadMaxX);
            SetProperty(root, "CadMinY", station.CadMinY);
            SetProperty(root, "CadMaxY", station.CadMaxY);

            var existingPoints = root[FindPropertyName(root, "Points") ?? "Points"] as JArray;
            var points = new JArray();
            foreach (var point in station.Points)
            {
                var existing = existingPoints?
                    .OfType<JObject>()
                    .FirstOrDefault(item => string.Equals(
                        item.Value<string>(FindPropertyName(item, "Id") ?? "Id"),
                        point.Id,
                        StringComparison.OrdinalIgnoreCase));
                var pointObject = existing is null ? new JObject() : (JObject)existing.DeepClone();
                SetProperty(pointObject, "Id", point.Id);
                SetProperty(pointObject, "Name", point.Name);
                SetProperty(pointObject, "CadX", point.CadX);
                SetProperty(pointObject, "CadY", point.CadY);
                SetProperty(pointObject, "Enabled", point.Enabled);
                points.Add(pointObject);
            }

            SetProperty(root, "Points", points);

            var existingLabels = root[FindPropertyName(root, "Labels") ?? "Labels"] as JArray;
            var labels = new JArray();
            foreach (var label in station.Labels)
            {
                var existing = existingLabels?
                    .OfType<JObject>()
                    .FirstOrDefault(item => string.Equals(
                        item.Value<string>(FindPropertyName(item, "Id") ?? "Id"),
                        label.Id,
                        StringComparison.OrdinalIgnoreCase));
                var labelObject = existing is null ? new JObject() : (JObject)existing.DeepClone();
                SetProperty(labelObject, "Id", label.Id);
                SetProperty(labelObject, "Text", label.Text);
                SetProperty(labelObject, "CadX", label.CadX);
                SetProperty(labelObject, "CadY", label.CadY);
                SetProperty(labelObject, "Rotation", label.Rotation);
                SetProperty(labelObject, "TextHeight", label.TextHeight);
                SetProperty(labelObject, "Enabled", label.Enabled);
                labels.Add(labelObject);
            }

            SetProperty(root, "Labels", labels);

            var existingDevices = root[FindPropertyName(root, "Devices") ?? "Devices"] as JArray;
            var devices = new JArray();
            foreach (var device in station!.Devices ?? Array.Empty<DeviceConfig>())
            {
                var existing = existingDevices?
                    .OfType<JObject>()
                    .FirstOrDefault(item => string.Equals(
                        item.Value<string>(FindPropertyName(item, "Id") ?? "Id"),
                        device.Id,
                        StringComparison.OrdinalIgnoreCase));
                var deviceObject = existing is null ? new JObject() : (JObject)existing.DeepClone();
                SetProperty(deviceObject, "Id", device.Id);
                SetProperty(deviceObject, "Name", device.Name);
                SetProperty(deviceObject, "Type", GetDeviceTypeName(device.Type));
                SetProperty(deviceObject, "StationId", device.StationId);
                SetProperty(deviceObject, "RfidStationId", device.RfidStationId is null ? JValue.CreateNull() : new JValue(device.RfidStationId));
                SetProperty(deviceObject, "CadX", device.CadX);
                SetProperty(deviceObject, "CadY", device.CadY);
                RemoveProperties(
                    deviceObject,
                    "IpAddress", "IP", "Port", "DestinationAddress", "DestinationPort",
                    "ProtocolAddress", "Address", "EndpointKey", "StationAddress",
                    "CommunicationState", "Status", "RuntimeState", "LastRequest", "LastResponse",
                    "LastRequestAt", "LastResponseAt", "CurrentHeadRfid", "DetectedVehicleCount",
                    "AlarmMessage", "WarningMessages", "LifecycleState", "VisualState");
                SetProperty(deviceObject, "Enabled", device.Enabled);
                devices.Add(deviceObject);
            }

            SetProperty(root, "Devices", devices);
            File.WriteAllText(stationPath, Newtonsoft.Json.JsonConvert.SerializeObject(root, Newtonsoft.Json.Formatting.Indented), new System.Text.UTF8Encoding(false));
            _logger.Information($"站场配置保存成功：{stationPath}");
            return ProjectConfigSaveResult.Success();
        }
        catch (Newtonsoft.Json.JsonException exception)
        {
            return SaveFailure(errors, $"站场配置 JSON 无效：{exception.Message}", exception);
        }
        catch (JsonException exception)
        {
            return SaveFailure(errors, $"站场配置 JSON 无效：{exception.Message}", exception);
        }
        catch (IOException exception)
        {
            return SaveFailure(errors, $"保存站场配置失败：{exception.Message}", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            return SaveFailure(errors, $"无权保存站场配置：{exception.Message}", exception);
        }
    }

    public async Task<ProjectConfigSaveResult> SaveRfidSettingsAsync(
        string projectDirectory,
        RfidSettings settings,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            return SaveFailure(errors, "项目目录不能为空。");
        }
        if (settings is null)
        {
            return SaveFailure(errors, "RFID设置不能为空。");
        }
        errors.AddRange(settings.Validate());
        if (errors.Count > 0)
        {
            return ProjectConfigSaveResult.Failure(errors);
        }

        var manifestPath = Path.Combine(projectDirectory, "project.json");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = JObject.Parse(File.ReadAllText(manifestPath));
            SetProperty(root, "RfidSettings", JObject.FromObject(settings));
            using (var writer = File.CreateText(manifestPath))
            {
                await writer.WriteAsync(root.ToString());
            }
            _logger.Information("RFID设置保存成功。");
            return ProjectConfigSaveResult.Success();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Newtonsoft.Json.JsonException)
        {
            return SaveFailure(errors, $"保存RFID设置失败：{exception.Message}", exception);
        }
    }

    public async Task<ProjectConfigSaveResult> SaveRfidStationsAsync(
        string projectDirectory,
        IEnumerable<RfidStationConfig> stations,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            return SaveFailure(errors, "项目目录不能为空。");
        }
        if (stations is null)
        {
            return SaveFailure(errors, "RFID基站配置不能为空。");
        }

        var stationList = stations.ToArray();
        var currentProject = await LoadAsync(projectDirectory, cancellationToken);
        if (!currentProject.Succeeded || currentProject.Project is null)
        {
            return SaveFailure(
                errors,
                currentProject.Errors.Count == 0
                    ? "读取项目配置失败，无法校验 RFID 基站所属站场。"
                    : string.Join(Environment.NewLine, currentProject.Errors));
        }

        errors.AddRange(RfidStationOwnershipRules.Validate(
            currentProject.Project.Stations,
            stationList));
        ValidateRfidStations(stationList, errors);
        if (errors.Count > 0)
        {
            return ProjectConfigSaveResult.Failure(errors);
        }

        var manifestPath = Path.Combine(projectDirectory, "project.json");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = JObject.Parse(File.ReadAllText(manifestPath));
            var stationArray = new JArray(stationList.Select(ToManifestObject));
            SetProperty(root, "RfidStations", stationArray);
            using (var writer = File.CreateText(manifestPath))
            {
                await writer.WriteAsync(root.ToString(Newtonsoft.Json.Formatting.Indented));
            }

            _logger.Information("RFID基站配置保存成功。");
            return ProjectConfigSaveResult.Success();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Newtonsoft.Json.JsonException)
        {
            return SaveFailure(errors, $"保存RFID基站配置失败：{exception.Message}", exception);
        }
    }

    public async Task<ProjectConfigSaveResult> SaveYardCommunicationsAsync(
        string projectDirectory,
        IEnumerable<YardCommunicationConfig> configurations,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            return SaveFailure(errors, "项目目录不能为空。");
        }
        if (configurations is null)
        {
            return SaveFailure(errors, "站场通信接口配置不能为空。");
        }

        var configurationList = configurations.ToArray();
        var currentProject = await LoadAsync(projectDirectory, cancellationToken);
        if (!currentProject.Succeeded || currentProject.Project is null)
        {
            return SaveFailure(
                errors,
                currentProject.Errors.Count == 0
                    ? "读取项目配置失败，无法保存站场通信接口。"
                    : string.Join(Environment.NewLine, currentProject.Errors));
        }

        ValidateYardCommunications(currentProject.Project.Stations, configurationList, errors);
        if (errors.Count > 0)
        {
            return ProjectConfigSaveResult.Failure(errors);
        }

        var manifestPath = Path.Combine(projectDirectory, "project.json");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = JObject.Parse(File.ReadAllText(manifestPath));
            SetProperty(root, "YardCommunications", new JArray(configurationList.Select(ToManifestObject)));
            using (var writer = File.CreateText(manifestPath))
            {
                await writer.WriteAsync(root.ToString(Newtonsoft.Json.Formatting.Indented));
            }

            _logger.Information("站场通信接口配置保存成功。");
            return ProjectConfigSaveResult.Success();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Newtonsoft.Json.JsonException)
        {
            return SaveFailure(errors, $"保存站场通信接口配置失败：{exception.Message}", exception);
        }
    }

    public async Task<ProjectConfigSaveResult> SaveYardAlarmForwardsAsync(
        string projectDirectory,
        IEnumerable<YardAlarmForwardConfig> configurations,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            return SaveFailure(errors, "项目目录不能为空。");
        }
        if (configurations is null)
        {
            return SaveFailure(errors, "报警转发配置不能为空。");
        }

        var configurationList = configurations.ToArray();
        var currentProject = await LoadAsync(projectDirectory, cancellationToken);
        if (!currentProject.Succeeded || currentProject.Project is null)
        {
            return SaveFailure(
                errors,
                currentProject.Errors.Count == 0
                    ? "读取项目配置失败，无法保存报警转发配置。"
                    : string.Join(Environment.NewLine, currentProject.Errors));
        }

        ValidateYardAlarmForwards(
            currentProject.Project.Stations,
            configurationList,
            currentProject.Project.UsesLegacySharedListener,
            errors);
        if (errors.Count > 0)
        {
            return ProjectConfigSaveResult.Failure(errors);
        }

        var manifestPath = Path.Combine(projectDirectory, "project.json");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = JObject.Parse(File.ReadAllText(manifestPath));
            SetProperty(root, "YardAlarmForwards", new JArray(configurationList.Select(ToManifestObject)));
            using (var writer = File.CreateText(manifestPath))
            {
                await writer.WriteAsync(root.ToString(Newtonsoft.Json.Formatting.Indented));
            }

            _logger.Information("站场报警转发配置保存成功。");
            return ProjectConfigSaveResult.Success();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Newtonsoft.Json.JsonException)
        {
            return SaveFailure(errors, $"保存站场报警转发配置失败：{exception.Message}", exception);
        }
    }

    private async Task<string?> ResolveStationPathAsync(
        string projectDirectory,
        string stationId,
        CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(projectDirectory, "project.json");
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        using var reader = File.OpenText(manifestPath);
        var json = await reader.ReadToEndAsync();
        cancellationToken.ThrowIfCancellationRequested();
        var manifest = JsonSerializer.Deserialize<ProjectManifest>(json, _jsonOptions);
        var reference = manifest?.Stations.FirstOrDefault(item =>
            item.Id.Equals(stationId, StringComparison.OrdinalIgnoreCase));
        return reference is null ? null : Path.Combine(projectDirectory, reference.ConfigFile);
    }

    private ProjectConfigSaveResult SaveFailure(
        List<string> errors,
        string message,
        Exception? exception = null)
    {
        AddError(errors, message, exception);
        return ProjectConfigSaveResult.Failure(errors);
    }

    private static string GetDeviceTypeName(DeviceType type) =>
        type == DeviceType.RfidStation ? nameof(DeviceType.RfidStation) : type.ToString();

    private static string GetBindingKey(RfidStationBindingLocation binding) =>
        $"{binding.YardId}\u001F{binding.DeviceId}\u001F{binding.RfidStationId}";

    private static void ValidateRfidStations(
        IReadOnlyList<RfidStationConfig> stations,
        ICollection<string> errors)
    {
        foreach (var stationId in RfidStationConfigurationRules.FindDuplicateStationIds(stations))
        {
            errors.Add($"RFID基站编号重复：{stationId}。");
        }

        foreach (var duplicateError in RfidStationConfig.FindDuplicateCommunicationKeys(stations))
        {
            errors.Add(duplicateError);
        }

        foreach (var station in stations.Where(station => station is not null && station.Enabled))
        {
            if (string.IsNullOrWhiteSpace(station.StationId))
            {
                errors.Add("启用的RFID基站编号不能为空。");
            }
            if (string.IsNullOrWhiteSpace(station.Name))
            {
                errors.Add($"RFID基站名称不能为空：{station.StationId}。");
            }
            if (!IPAddress.TryParse(station.IpAddress, out var address) ||
                IPAddress.None.Equals(address) || IPAddress.Any.Equals(address) || IPAddress.IPv6Any.Equals(address))
            {
                errors.Add($"RFID基站IP地址无效：{station.StationId}。");
            }
            if (station.Port is < 1 or > 65535)
            {
                errors.Add($"RFID基站端口无效：{station.StationId}。");
            }
            if (station.ProtocolAddress == 0)
            {
                errors.Add($"RFID基站协议地址不能为00：{station.StationId}。");
            }
        }
    }

    private static void ValidateUniqueRfidStationIds(
        IReadOnlyList<RfidStationConfig> stations,
        ICollection<string> errors)
    {
        foreach (var stationId in RfidStationConfigurationRules.FindDuplicateStationIds(stations))
        {
            errors.Add($"RFID基站编号重复：{stationId}。");
        }
    }

    private static void ValidateYardCommunications(
        IReadOnlyList<StationConfig> yards,
        IReadOnlyList<YardCommunicationConfig> configurations,
        ICollection<string> errors)
    {
        var yardIds = new HashSet<string>(
            yards.Where(yard => yard is not null && !string.IsNullOrWhiteSpace(yard.Id))
                .Select(yard => yard.Id.Trim()),
            StringComparer.OrdinalIgnoreCase);
        var seenYardIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenEndpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var configuration in configurations.Where(item => item is not null))
        {
            foreach (var validationError in configuration.Validate())
            {
                errors.Add(validationError);
            }
            var yardId = configuration.YardId?.Trim() ?? string.Empty;
            if (!configuration.IsLegacySharedListener && !seenYardIds.Add(yardId))
            {
                errors.Add($"站场通信接口所属站场重复：{yardId}。");
            }
            if (!configuration.IsLegacySharedListener && yardId.Length > 0 && !yardIds.Contains(yardId))
            {
                errors.Add($"站场通信接口所属站场不存在：{yardId}。");
            }

            if (!configuration.Enabled || !configuration.TryResolveEndpoint(out var endpoint))
            {
                continue;
            }

            var endpointKey = $"{endpoint.Address}|{endpoint.Port}";
            if (!seenEndpoints.Add(endpointKey))
            {
                errors.Add($"启用的站场监听端点重复：{endpoint}，监听端点重复。");
            }
        }
    }

    private static void ValidateYardAlarmForwards(
        IReadOnlyList<StationConfig> yards,
        IReadOnlyList<YardAlarmForwardConfig> configurations,
        bool isLegacySharedListener,
        ICollection<string> errors)
    {
        var yardIds = new HashSet<string>(
            yards.Where(yard => yard is not null && !string.IsNullOrWhiteSpace(yard.Id))
                .Select(yard => yard.Id.Trim()),
            StringComparer.OrdinalIgnoreCase);
        var seenYardIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var configuration in configurations.Where(item => item is not null))
        {
            foreach (var validationError in configuration.Validate())
            {
                errors.Add(validationError);
            }

            var yardId = configuration.YardId?.Trim() ?? string.Empty;
            if (!seenYardIds.Add(yardId))
            {
                errors.Add($"报警转发所属站场重复：{yardId}。");
            }
            if (yardId.Length > 0 && !yardIds.Contains(yardId))
            {
                errors.Add($"报警转发所属站场不存在：{yardId}。");
            }
            if (isLegacySharedListener && configuration.Enabled)
            {
                errors.Add("旧项目必须迁移为独立站场通信配置后才能启用报警转发。");
            }
        }
    }

    private static JObject ToManifestObject(RfidStationConfig station)
    {
        var result = new JObject
        {
            ["StationId"] = station.StationId,
            ["Name"] = station.Name,
            ["IpAddress"] = station.IpAddress,
            ["Port"] = station.Port,
            ["ProtocolAddress"] = station.ProtocolAddress,
            ["Enabled"] = station.Enabled,
            ["Mode"] = station.Mode,
            ["CommandBytes"] = ToByteArrayToken(station.CommandBytes),
            ["RequestPayload"] = ToByteArrayToken(station.RequestPayload),
            ["CrcHigh"] = station.CrcHigh,
            ["CrcLow"] = station.CrcLow
        };
        if (!string.IsNullOrWhiteSpace(station.YardId))
        {
            result["YardId"] = station.YardId!.Trim();
        }

        return result;
    }

    private static JObject ToManifestObject(YardCommunicationConfig configuration) => new()
    {
        ["YardId"] = configuration.YardId.Trim(),
        ["ListenIp"] = configuration.ListenIp.Trim(),
        ["ListenPort"] = configuration.ListenPort,
        ["Enabled"] = configuration.Enabled
    };

    private static JObject ToManifestObject(YardAlarmForwardConfig configuration) => new()
    {
        ["YardId"] = configuration.YardId.Trim(),
        ["Enabled"] = configuration.Enabled,
        ["TargetIp"] = configuration.TargetIp?.Trim() ?? string.Empty,
        ["TargetPort"] = configuration.TargetPort
    };

    private static JArray ToByteArrayToken(IEnumerable<byte>? values) =>
        new((values ?? Array.Empty<byte>()).Select(value => new JValue(value)));

    private static void SetProperty(JObject target, string preferredName, object? value)
    {
        var propertyName = FindPropertyName(target, preferredName) ?? preferredName;
        target[propertyName] = value is JToken token ? token : JToken.FromObject(value!);
    }

    private static void RemoveProperties(JObject target, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            var actualName = FindPropertyName(target, propertyName);
            if (actualName is not null)
            {
                target.Remove(actualName);
            }
        }
    }

    private static string? FindPropertyName(JObject target, string propertyName) =>
        target.Properties().FirstOrDefault(item =>
            item.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))?.Name;

    private static string NormalizeLegacyRfidSettings(string json)
    {
        var root = JObject.Parse(json);
        if (root[FindPropertyName(root, "RfidSettings") ?? "RfidSettings"] is not JObject settings)
        {
            return json;
        }

        MigrateProperty(settings, "ExpectedWagonCount", "ExpectedVehicleCount");
        MigrateProperty(settings, "InterWagonTimeoutSeconds", "InterVehicleTimeoutSeconds");
        return root.ToString(Newtonsoft.Json.Formatting.None);
    }

    private static void MigrateProperty(JObject target, string legacyName, string currentName)
    {
        var legacyPropertyName = FindPropertyName(target, legacyName);
        var currentPropertyName = FindPropertyName(target, currentName);
        if (currentPropertyName is null && legacyPropertyName is not null)
        {
            target[currentName] = target[legacyPropertyName]!.DeepClone();
        }

        if (legacyPropertyName is not null)
        {
            target.Remove(legacyPropertyName);
        }
    }

    private ProjectConfigLoadResult Failure(
        List<string> errors,
        string message,
        Exception? exception = null)
    {
        AddError(errors, message, exception);
        return ProjectConfigLoadResult.Failure(errors);
    }

    private void AddError(List<string> errors, string message, Exception? exception = null)
    {
        errors.Add(message);
        _logger.Error(message, exception);
    }

    private sealed class ProjectManifest
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string DefaultStationId { get; set; } = string.Empty;

        public RfidSettings? RfidSettings { get; set; }

        public IReadOnlyList<RfidStationManifest>? RfidStations { get; set; }

        public IReadOnlyList<YardCommunicationManifest>? YardCommunications { get; set; }

        public IReadOnlyList<YardAlarmForwardManifest>? YardAlarmForwards { get; set; }

        public sealed class YardCommunicationManifest
        {
            public string YardId { get; set; } = string.Empty;
            public string ListenIp { get; set; } = string.Empty;
            public int ListenPort { get; set; }
            public bool Enabled { get; set; } = true;

            public YardCommunicationConfig ToConfig() => new()
            {
                YardId = YardId,
                ListenIp = ListenIp,
                ListenPort = ListenPort,
                Enabled = Enabled
            };
        }

        public sealed class YardAlarmForwardManifest
        {
            public string YardId { get; set; } = string.Empty;
            public bool Enabled { get; set; }
            public string TargetIp { get; set; } = string.Empty;
            public int TargetPort { get; set; }

            public YardAlarmForwardConfig ToConfig() => new()
            {
                YardId = YardId,
                Enabled = Enabled,
                TargetIp = TargetIp,
                TargetPort = TargetPort
            };
        }

    public sealed class RfidStationManifest
    {
        public string StationId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? YardId { get; set; }
        public string IpAddress { get; set; } = string.Empty;
        public int Port { get; set; }
        public byte ProtocolAddress { get; set; }

        // Legacy fields remain readable for existing project manifests.
        public byte Address { get; set; }
        public bool Enabled { get; set; } = true;
        public byte Mode { get; set; } = 0x04;
        public byte[] CommandBytes { get; set; } = new byte[4];
        public byte[] RequestPayload { get; set; } = new byte[28];
        public byte CrcHigh { get; set; }
        public byte CrcLow { get; set; }
        public string DestinationAddress { get; set; } = string.Empty;
        public int DestinationPort { get; set; }

        public RfidStationConfig ToConfig()
        {
            var protocolAddress = ProtocolAddress == 0 ? Address : ProtocolAddress;
            var ipAddress = string.IsNullOrWhiteSpace(IpAddress) ? DestinationAddress : IpAddress;
            var port = Port > 0 ? Port : DestinationPort;
            var stationId = string.IsNullOrWhiteSpace(StationId) ? $"RFID-{protocolAddress:X2}" : StationId;
            var name = string.IsNullOrWhiteSpace(Name) ? stationId : Name;
            var endpoint = IPAddress.TryParse(ipAddress, out var address) && port is >= 1 and <= 65535
                ? new IPEndPoint(address, port)
                : new IPEndPoint(IPAddress.None, 0);
            return new RfidStationConfig
            {
                StationId = stationId,
                Name = name,
                YardId = YardId,
                IpAddress = ipAddress,
                Port = port,
                ProtocolAddress = protocolAddress,
                Enabled = Enabled,
                Mode = Mode,
                CommandBytes = CommandBytes,
                RequestPayload = RequestPayload,
                CrcHigh = CrcHigh,
                CrcLow = CrcLow,
                DestinationEndpoint = endpoint
            };
        }
    }

        public IReadOnlyList<StationReference> Stations { get; set; } = new List<StationReference>();
    }

    private sealed class StationReference
    {
        public string Id { get; set; } = string.Empty;

        public string ConfigFile { get; set; } = string.Empty;
    }
}
