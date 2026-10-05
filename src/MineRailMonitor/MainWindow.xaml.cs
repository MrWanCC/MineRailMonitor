using System.IO;
using System.Linq;
using System.Net;
using System.Configuration;
using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using MineRailMonitor.Core.Acceptance;
using MineRailMonitor.Core.Interfaces;
using MineRailMonitor.Core.Models;
using MineRailMonitor.Core.Communication;
using MineRailMonitor.Core.ExternalAlarm;
using MineRailMonitor.Core.Protocol;
using MineRailMonitor.Core.Recognition;
using MineRailMonitor.Core.Services;
using MineRailMonitor.Infrastructure.Configuration;
using MineRailMonitor.Infrastructure.BlackBox;
using MineRailMonitor.Infrastructure.ExternalData;
using MineRailMonitor.Infrastructure.Logging;
using MineRailMonitor.Infrastructure.Persistence;
using MineRailMonitor.Pages;

namespace MineRailMonitor;

public partial class MainWindow : Window
{
    private readonly IProjectConfigService _configService;
    private readonly AdminModeService _adminModeService;
    private readonly string _projectDirectory;
    private readonly AcceptanceCommandLineOptions _acceptanceOptions;
    private readonly bool _verboseRfidFrameLoggingEnabled;
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private MonitorPage? _monitorPage;
    private readonly CommunicationPage _communicationPage;
    private readonly RunLogPage _runLogPage;
    private readonly IOperationRecordStore _operationRecordStore;
    private RfidFrameParser _rfidFrameParser;
    private SettingsPage? _settingsPage;
    private HistoryPage? _historyPage;
    private AlarmHistoryPage? _alarmHistoryPage;
    private RfidStatisticsPage? _statisticsPage;
    private YardCommunicationManager? _yardCommunicationManager;
    private readonly SqlitePassageRecordStore _passageRecordStore;
    private readonly RawPacketBlackBoxWriter _rawPacketBlackBoxWriter;
    private AcceptanceRuntimeStateWriter? _acceptanceRuntimeStateWriter;
    private DateTimeOffset _lastStatisticsRefresh = DateTimeOffset.MinValue;
    private DateTimeOffset _lastRecentAlarmRefresh = DateTimeOffset.MinValue;
    private ProjectConfig? _loadedProject;
    private readonly CurrentYardContext _currentYardContext = new();
    private IYardRfidStationResolver? _yardRfidStationResolver;
    private Button? _activeNavigationButton;
    private Button? _activeStationButton;
    private bool _allowWindowClose;
    private bool _closeInProgress;
    private bool _rfidListenerHealthy;
    private bool _externalInterfaceAvailable;
    private readonly ExternalAlarmStatusChangeTracker _externalAlarmStatusChangeTracker = new();

    private enum StatusIndicatorState
    {
        Healthy,
        Warning,
        Error,
        Unavailable
    }

    private const int WM_GETMINMAXINFO = 0x0024;
    private const uint MonitorDefaultToNearest = 0x00000002;

    public MainWindow(SqlitePassageRecordStore passageRecordStore)
    {
        _passageRecordStore = passageRecordStore ?? throw new ArgumentNullException(nameof(passageRecordStore));
        InitializeComponent();
        _acceptanceOptions = ((App)Application.Current).AcceptanceOptions;
#if DEBUG
        const bool debugBuild = true;
#else
        const bool debugBuild = false;
#endif
        var verboseLoggingSetting = ConfigurationManager.AppSettings["RfidVerboseLogging"];
        var explicitlyEnabled = bool.TryParse(verboseLoggingSetting, out var configuredVerboseLogging) &&
                                configuredVerboseLogging;
        _verboseRfidFrameLoggingEnabled = RfidFrameTextLoggingPolicy.ShouldWriteVerboseFrameLogs(
            _acceptanceOptions.Enabled,
            debugBuild,
            explicitlyEnabled);
        var appPaths = ((App)Application.Current).Paths;
        _externalInterfaceAvailable = false;
        _projectDirectory = ProjectDirectoryResolver.Resolve(
            appPaths.ProjectsDirectory,
            _acceptanceOptions.Enabled);
        if (_acceptanceOptions.Enabled)
        {
            WindowState = WindowState.Minimized;
            ShowInTaskbar = false;
        }
        _adminModeService = ((App)Application.Current).AdminModeService;
        _adminModeService.PropertyChanged += OnAdminModeStateChanged;
        _currentYardContext.PropertyChanged += OnCurrentYardContextChanged;
        UpdateAdminModeBanner();
        _configService = new ProjectConfigService(((App)Application.Current).Logger);
        _communicationPage = new CommunicationPage();
        var passageEventLogDirectory = _acceptanceOptions.Enabled
            ? _acceptanceOptions.LogDirectory!
            : appPaths.LogsDirectory;
        _operationRecordStore = new OperationRecordJsonlStore(passageEventLogDirectory);
        _runLogPage = new RunLogPage(
            new PassageEventJsonlQueryService(passageEventLogDirectory),
            _operationRecordStore,
            _passageRecordStore,
            () => _yardCommunicationManager?.GetPollingStatuses() ?? Array.Empty<RfidStationPollingStatus>(),
            _adminModeService);
        var blackBoxRootDirectory = _acceptanceOptions.Enabled
            ? Path.Combine(_acceptanceOptions.LogDirectory!, "BlackBox")
            : appPaths.BlackBoxDirectory;
        _rawPacketBlackBoxWriter = new RawPacketBlackBoxWriter(blackBoxRootDirectory);
        _communicationPage.OpenBlackBoxDirectoryRequested += OnOpenBlackBoxDirectoryRequested;
        _communicationPage.SetBlackBoxStatus(_rawPacketBlackBoxWriter.GetSnapshot());
        _communicationPage.SetAdminMode(_adminModeService.IsAdmin);
        _rfidFrameParser = new RfidFrameParser(new RfidFrameParserOptions
        {
            EmptyRfidValue = ReadEmptyRfidValue()
        });
        _clockTimer.Tick += OnClockTick;
        Closed += OnWindowClosed;
        SourceInitialized += OnSourceInitialized;
        SelectNavigationButton(MonitorNavButton);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(WindowProc);
        }
    }

    private static IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WM_GETMINMAXINFO || lParam == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return IntPtr.Zero;
        }

        var minMaxInfo = (MinMaxInfo)Marshal.PtrToStructure(lParam, typeof(MinMaxInfo))!;
        minMaxInfo.MaxPosition.X = monitorInfo.rcWork.Left - monitorInfo.rcMonitor.Left;
        minMaxInfo.MaxPosition.Y = monitorInfo.rcWork.Top - monitorInfo.rcMonitor.Top;
        minMaxInfo.MaxSize.X = monitorInfo.rcWork.Right - monitorInfo.rcWork.Left;
        minMaxInfo.MaxSize.Y = monitorInfo.rcWork.Bottom - monitorInfo.rcWork.Top;
        Marshal.StructureToPtr(minMaxInfo, lParam, true);
        handled = true;
        return IntPtr.Zero;
    }

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        OnWindowStateChanged(this, EventArgs.Empty);
        ApplyDwmBorderFallback();
        UpdateClock();
        _clockTimer.Start();
        await LoadProjectAsync();
    }

    private void OnWindowStateChanged(object sender, EventArgs e)
    {
        var chrome = WindowChrome.GetWindowChrome(this);
        if (chrome is null)
        {
            return;
        }

        // Keep the resize affordance in the normal state, but do not let the
        // custom border extend a maximized window beyond the Windows work area.
        chrome.ResizeBorderThickness = new Thickness(6);
        ApplyDwmBorderFallback();
    }

    private async Task LoadProjectAsync()
    {
        var result = await _configService.LoadAsync(_projectDirectory);
        if (!result.Succeeded || result.Project is null)
        {
            var productionProjectMissing = !_acceptanceOptions.Enabled && result.Errors.Any(error =>
                error.StartsWith("未找到项目配置文件：", StringComparison.Ordinal));
            if (productionProjectMissing)
            {
                PageContent.Content = new PlaceholderPage(
                    "现场项目配置缺失",
                    "请配置 Projects/Default/project.json 后重新启动。");
                return;
            }

            var error = result.Errors.Count == 0
                ? "项目配置未返回有效内容。"
                : string.Join(Environment.NewLine, result.Errors);
            PageContent.Content = new PlaceholderPage("项目配置加载失败", error);
            return;
        }

        _loadedProject = result.Project;
        var legacyStationIdMigration = RfidStationIdentity.BuildLegacyMigrationMap(_loadedProject.RfidStations);
        if (legacyStationIdMigration.Count > 0)
        {
            var migratedRecordCount = _passageRecordStore.MigrateStationIds(legacyStationIdMigration);
            ((App)Application.Current).Logger.Information(
                $"RFID基站编号已规范化：{legacyStationIdMigration.Count} 个别名，历史记录更新 {migratedRecordCount} 条。");
        }
        if (_acceptanceOptions.Enabled)
        {
            result.Project.Vehicles = CreateAcceptanceVehicles();
            result.Project.RfidSettings.ExpectedVehicleCount = 11;
        }
        var runtimeSettings = GetRuntimeSettings(result.Project);
        _rfidFrameParser = new RfidFrameParser(new RfidFrameParserOptions
        {
            EmptyRfidValue = runtimeSettings.EmptyRfidValue
        });
        var settingsStations = _acceptanceOptions.Enabled ? CreateAcceptanceStations() : result.Project.RfidStations;
        _yardRfidStationResolver = new YardRfidStationResolver(result.Project.Stations, settingsStations);
        if (_acceptanceOptions.Enabled)
        {
            _currentYardContext.SelectGlobal();
        }
        else if (result.Project.Stations.Any(item =>
                     string.Equals(item.Id, result.Project.DefaultStationId, StringComparison.OrdinalIgnoreCase)))
        {
            _currentYardContext.SelectYard(result.Project.DefaultStationId);
        }
        _settingsPage = new SettingsPage(
            runtimeSettings,
            _adminModeService,
            settingsStations,
            stationsEditable: !_acceptanceOptions.Enabled,
            isRfidStationBound: IsRfidStationBound,
            yards: result.Project.Stations,
            bindingSaveRequested: SaveRfidBindingAsync,
            viewMapPointRequested: ViewRfidMapPoint,
            yardCommunications: result.Project.YardCommunications,
            yardAlarmForwards: result.Project.YardAlarmForwards,
            vehicles: result.Project.Vehicles,
            operationRecordStore: _operationRecordStore);
        _settingsPage.SetCommunicationTestContent(_communicationPage);
        _settingsPage.SaveRequested += SaveRfidSettingsAsync;
        _settingsPage.StationsSaveRequested += SaveRfidStationsAsync;
        _settingsPage.SaveYardCommunicationsRequested += SaveYardCommunicationsAsync;
        _settingsPage.SaveYardAlarmForwardsRequested += SaveYardAlarmForwardsAsync;
        _settingsPage.VehiclesSaveRequested += SaveVehiclesAsync;
        _settingsPage.AdminModeExitRequested += ExitAdminModeAsync;
        _settingsPage.BackupRequested += CreateManualBackupAsync;
        await RecreateYardCommunicationManagerAsync(result.Project, settingsStations, runtimeSettings);
        _acceptanceRuntimeStateWriter?.Write("loaded");
        _historyPage = new HistoryPage(
            _passageRecordStore,
            _acceptanceOptions.Enabled ? settingsStations : result.Project.RfidStations);
        _alarmHistoryPage = new AlarmHistoryPage(
            _passageRecordStore,
            _acceptanceOptions.Enabled ? settingsStations : result.Project.RfidStations,
            AcknowledgeAlarm);
        _statisticsPage = new RfidStatisticsPage(_passageRecordStore, _passageRecordStore);
        _historyPage.SetYardOptions(result.Project.Stations);
        _alarmHistoryPage.SetYardOptions(result.Project.Stations);
        _communicationPage.SetYardOptions(result.Project.Stations);
        _communicationPage.SetExternalAlarmConfigurations(result.Project.YardCommunications, result.Project.YardAlarmForwards);
        _communicationPage.ConfigureStations(settingsStations, SendCommunicationTestAsync);
        _runLogPage.SetRfidStations(settingsStations);
        StationButtonsPanel.Children.Clear();
        foreach (var station in result.Project.Stations)
        {
            var button = new Button
            {
                Content = FormatStationName(station),
                Tag = station.Id,
                Style = (Style)FindResource("StationButtonStyle")
            };
            button.Click += OnStationClick;
            StationButtonsPanel.Children.Add(button);
        }

        _monitorPage = new MonitorPage(
            _configService,
            _projectDirectory,
            _adminModeService,
            result.Project.Stations);
        _monitorPage.AlarmMoreRequested += OnMonitorAlarmMoreRequested;
        _monitorPage.MapAnnotationsSaved += OnMonitorMapAnnotationsSaved;
        _monitorPage.SetRfidStations(settingsStations);
        _monitorPage.SetSystemHealth(databaseHealthy: true, externalInterfaceAvailable: _externalInterfaceAvailable);
        _monitorPage.SetSystemRfidStatus(_rfidListenerHealthy);
        _monitorPage.ResetRequested += (_, _) => _monitorPage.Viewport.ResetView();
        _monitorPage.AddSystemEvent("配置", "配置加载成功");
        PageContent.Content = _monitorPage;
        ShowStation(result.Project.DefaultStationId);
        ApplyCurrentYardDisplayScope();
        UpdateRfidRuntimeUi();
        RefreshHistoricalStatistics();
        if (_acceptanceOptions.Enabled)
        {
            AtomicFileWriter.WriteAllText(_acceptanceOptions.ReadyFile!, "{\"status\":\"ready\"}");
        }
    }

    private async void OnNavigationClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string page)
        {
            return;
        }

        if (page != "Monitor" && _monitorPage is not null &&
            !await _monitorPage.TryLeaveMapEditingAsync("离开地图标注"))
        {
            return;
        }

        if (page != "Settings" && _settingsPage is not null &&
            !await _settingsPage.TryLeaveAsync("离开系统设置"))
        {
            return;
        }

        SelectNavigationButton(button);

        if (page == "Monitor" && _monitorPage is not null)
        {
            PageContent.Content = _monitorPage;
            return;
        }

        if (page == "Rfid" && _statisticsPage is not null)
        {
            _statisticsPage.Refresh();
            PageContent.Content = _statisticsPage;
            return;
        }

        if (page == "Settings" && _settingsPage is not null)
        {
            _settingsPage.RefreshBindingOverview();
            _settingsPage.RefreshOperationLogs();
            PageContent.Content = _settingsPage;
            return;
        }

        if (page == "History" && _historyPage is not null)
        {
            ApplyCurrentYardDisplayScope();
            _historyPage.Refresh();
            PageContent.Content = _historyPage;
            return;
        }

        if (page == "RunLogs")
        {
            _runLogPage.Refresh();
            PageContent.Content = _runLogPage;
            return;
        }

        if (page == "Alarms" && _alarmHistoryPage is not null)
        {
            ApplyCurrentYardDisplayScope();
            _alarmHistoryPage.Refresh();
            PageContent.Content = _alarmHistoryPage;
            return;
        }

        var title = page switch
        {
            "Rfid" => "统计分析",
            "Query" => "数据查询",
            "Alarms" => "报警记录",
            "History" => "历史查询",
            "RunLogs" => "运行日志",
            "Settings" => "系统设置",
            _ => "页面"
        };
        PageContent.Content = new PlaceholderPage(title, "本阶段仅提供页面骨架，业务功能将在后续阶段实现。");
    }

    private async void OnMonitorAlarmMoreRequested(object? sender, EventArgs e)
    {
        if (_alarmHistoryPage is null)
        {
            return;
        }

        if (_monitorPage is not null && !await _monitorPage.TryLeaveMapEditingAsync("查看报警记录"))
        {
            return;
        }

        if (_settingsPage is not null && !await _settingsPage.TryLeaveAsync("查看报警记录"))
        {
            return;
        }

        SelectNavigationButton(AlarmNavButton);
        _alarmHistoryPage.Refresh();
        PageContent.Content = _alarmHistoryPage;
    }

    private void OnMonitorMapAnnotationsSaved(object? sender, EventArgs e)
    {
        if (_loadedProject is null)
        {
            return;
        }

        _yardRfidStationResolver = new YardRfidStationResolver(
            _loadedProject.Stations,
            _loadedProject.RfidStations);
        _settingsPage?.RefreshBindingOverview();
        ApplyCurrentYardDisplayScope();
    }

    private async Task<bool> SaveRfidSettingsAsync(RfidSettings settings)
    {
        if (_loadedProject is null || _settingsPage is null || !_adminModeService.IsAdmin)
        {
            return false;
        }

        if (AreRfidSettingsEqual(_loadedProject.RfidSettings, settings))
        {
            _settingsPage.SetSaveResult("设置已保存。", false);
            return true;
        }

        var previousSettings = _loadedProject.RfidSettings;
        var result = await _configService.SaveRfidSettingsAsync(_projectDirectory, settings);
        if (!result.Succeeded)
        {
            _settingsPage.SetSaveResult(string.Join(Environment.NewLine, result.Errors), true);
            return false;
        }
        _loadedProject.RfidSettings = settings;
        RecordOperationChanges(ConfigurationOperationRecordFormatter.GetRfidSettingsChanges(previousSettings, settings));
        _rfidFrameParser = new RfidFrameParser(new RfidFrameParserOptions { EmptyRfidValue = settings.EmptyRfidValue });
        _yardCommunicationManager?.UpdateSettings(settings);
        if (_yardCommunicationManager is not null)
        {
            await _yardCommunicationManager.RestartAllAsync();
        }
        _settingsPage.SetSaveResult("设置已保存。", false);
        return true;
    }

    private static bool AreRfidSettingsEqual(RfidSettings left, RfidSettings right) =>
        left.PollIntervalMs == right.PollIntervalMs &&
        left.ExpectedVehicleCount == right.ExpectedVehicleCount &&
        left.InterVehicleTimeoutSeconds == right.InterVehicleTimeoutSeconds &&
        left.EmptyRfidValue == right.EmptyRfidValue;

    private async Task<bool> SaveVehiclesAsync(IReadOnlyList<VehicleConfig> vehicles)
    {
        if (_loadedProject is null || _settingsPage is null)
        {
            return false;
        }
        if (!_adminModeService.IsAdmin)
        {
            _settingsPage.SetVehicleSaveResult("请先进入管理员模式后再保存车辆配置。", true);
            return false;
        }
        if (_acceptanceOptions.Enabled)
        {
            _settingsPage.SetVehicleSaveResult("验收模式禁止保存正式车辆配置。", true);
            return false;
        }

        var previousVehicles = _loadedProject.Vehicles.ToArray();
        var result = await _configService.SaveVehiclesAsync(_projectDirectory, vehicles);
        if (!result.Succeeded)
        {
            _settingsPage.SetVehicleSaveResult(string.Join(Environment.NewLine, result.Errors), true);
            return false;
        }

        _loadedProject.Vehicles = vehicles.ToArray();
        RecordOperationChanges(ConfigurationOperationRecordFormatter.GetVehicleChanges(previousVehicles, _loadedProject.Vehicles));
        _yardCommunicationManager?.UpdateVehicleConfigurations(_loadedProject.Vehicles);
        _settingsPage.SetVehicleSaveResult("车辆编组配置已保存。", false);
        return true;
    }

    private async Task<bool> SaveYardCommunicationsAsync(IReadOnlyList<YardCommunicationConfig> configurations)
    {
        if (_loadedProject is null || _settingsPage is null || !_adminModeService.IsAdmin)
        {
            return false;
        }

        if (_acceptanceOptions.Enabled)
        {
            _settingsPage.SetSaveResult("验收模式禁止保存正式站场通信接口配置。", true);
            return false;
        }

        var previousConfigurations = _loadedProject.YardCommunications.ToArray();
        var result = await _configService.SaveYardCommunicationsAsync(_projectDirectory, configurations);
        if (!result.Succeeded)
        {
            _settingsPage.SetSaveResult(string.Join(Environment.NewLine, result.Errors), true);
            return false;
        }

        _loadedProject.YardCommunications = configurations.ToArray();
        RecordOperationChanges(ConfigurationOperationRecordFormatter.GetYardCommunicationChanges(
            previousConfigurations,
            _loadedProject.YardCommunications));
        _loadedProject.UsesLegacySharedListener = false;
        _communicationPage.SetExternalAlarmConfigurations(
            _loadedProject.YardCommunications,
            _loadedProject.YardAlarmForwards);
        if (_yardCommunicationManager is null)
        {
            await RecreateYardCommunicationManagerAsync(
                _loadedProject,
                _loadedProject.RfidStations,
                _loadedProject.RfidSettings);
        }
        else
        {
            var externalAlarmStatusCoordinators = CreateExternalAlarmStatusCoordinators(_loadedProject);
            _yardCommunicationManager.ApplyExternalAlarmStatusCoordinators(externalAlarmStatusCoordinators);
            await _yardCommunicationManager.ApplyConfigurationsAsync(configurations);
            _rfidListenerHealthy = IsCommunicationHealthy();
            UpdateRfidRuntimeUi();
        }
        _settingsPage.SetSaveResult("站场通信接口已保存，560/620 已按独立接口运行。", false);
        return true;
    }

    private async Task<bool> SaveYardAlarmForwardsAsync(IReadOnlyList<YardAlarmForwardConfig> configurations)
    {
        if (_loadedProject is null || _settingsPage is null || !_adminModeService.IsAdmin)
        {
            return false;
        }

        if (_acceptanceOptions.Enabled)
        {
            _settingsPage.SetSaveResult("验收模式禁止保存正式报警转发配置。", true);
            return false;
        }

        var previousConfigurations = _loadedProject.YardAlarmForwards.ToArray();
        var result = await _configService.SaveYardAlarmForwardsAsync(_projectDirectory, configurations);
        if (!result.Succeeded)
        {
            _settingsPage.SetSaveResult(string.Join(Environment.NewLine, result.Errors), true);
            return false;
        }

        _loadedProject.YardAlarmForwards = configurations.ToArray();
        RecordOperationChanges(ConfigurationOperationRecordFormatter.GetYardAlarmForwardChanges(
            previousConfigurations,
            _loadedProject.YardAlarmForwards));
        _communicationPage.SetExternalAlarmConfigurations(
            _loadedProject.YardCommunications,
            _loadedProject.YardAlarmForwards);
        var externalAlarmStatusCoordinators = CreateExternalAlarmStatusCoordinators(_loadedProject);
        _yardCommunicationManager?.ApplyExternalAlarmStatusCoordinators(externalAlarmStatusCoordinators);
        _externalInterfaceAvailable = externalAlarmStatusCoordinators.Count > 0;
        UpdateHeaderStatusIndicators();
        _monitorPage?.SetExternalInterfaceStatus(_externalInterfaceAvailable);
        _settingsPage.SetSaveResult("报警 UDP 转发配置已保存。", false);
        return true;
    }

    private async Task<bool> SaveRfidStationsAsync(IReadOnlyList<RfidStationConfig> stations)
    {
        if (_loadedProject is null || _settingsPage is null || !_adminModeService.IsAdmin)
        {
            return false;
        }
        if (_acceptanceOptions.Enabled)
        {
            _settingsPage.SetSaveResult("验收模式禁止保存正式基站配置。", true);
            return false;
        }

        var removedReferencedStationIds = RfidMapBindingResolver.FindRemovedReferencedStationIds(
            _loadedProject.Stations,
            stations);
        if (removedReferencedStationIds.Count > 0)
        {
            var dialog = new StyledMessageDialog(
                "无法删除 RFID 基站",
                "以下基站仍被地图标记引用，请先解除地图绑定后再删除：" + Environment.NewLine +
                string.Join(Environment.NewLine, removedReferencedStationIds.Select(stationId => $"• {stationId}")),
                MessageDialogKind.Warning)
            {
                Owner = this
            };
            dialog.ShowDialog();
            _settingsPage.SetSaveResult("存在地图绑定，无法删除 RFID 基站。请先解除地图绑定。", true);
            return false;
        }

        var previousStations = _loadedProject.RfidStations.ToArray();
        var requiresRuntimeRestart = RfidStationConfigurationChangeRules.RequiresRuntimeRestart(
            _loadedProject.RfidStations,
            stations);
        var result = await _configService.SaveRfidStationsAsync(_projectDirectory, stations);
        if (!result.Succeeded)
        {
            _settingsPage.SetSaveResult(string.Join(Environment.NewLine, result.Errors), true);
            return false;
        }

        _loadedProject.RfidStations = stations.ToArray();
        RecordOperationChanges(ConfigurationOperationRecordFormatter.GetStationChanges(previousStations, _loadedProject.RfidStations));
        _yardRfidStationResolver = new YardRfidStationResolver(_loadedProject.Stations, _loadedProject.RfidStations);
        _runLogPage.SetRfidStations(_loadedProject.RfidStations);
        ApplyCurrentYardDisplayScope();
        _communicationPage.ConfigureStations(_loadedProject.RfidStations, SendCommunicationTestAsync);
        _historyPage?.SetRfidStations(_loadedProject.RfidStations);
        _alarmHistoryPage?.SetRfidStations(_loadedProject.RfidStations);

        if (requiresRuntimeRestart)
        {
            var runtimeStations = _acceptanceOptions.Enabled
                ? CreateAcceptanceStations()
                : _loadedProject.RfidStations;
            await RecreateYardCommunicationManagerAsync(
                _loadedProject,
                runtimeStations,
                _loadedProject.RfidSettings);
        }
        UpdateRfidRuntimeUi();
        _settingsPage.SetSaveResult("设置与RFID基站配置已保存。", false);
        return true;
    }

    private bool IsRfidStationBound(string stationId)
    {
        if (_loadedProject is null || string.IsNullOrWhiteSpace(stationId))
        {
            return false;
        }

        var normalizedStationId = stationId.Trim();
        return _loadedProject.Stations
            .Where(station => station is not null)
            .SelectMany(station => station.Devices ?? Array.Empty<DeviceConfig>())
            .Any(device => device is not null &&
                          string.Equals(device.RfidStationId?.Trim(), normalizedStationId, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<bool> SaveRfidBindingAsync(StationConfig station)
    {
        if (_loadedProject is null || _settingsPage is null || !_adminModeService.IsAdmin)
        {
            return false;
        }

        if (_acceptanceOptions.Enabled)
        {
            _settingsPage.SetSaveResult("验收模式禁止保存地图绑定配置。", true);
            return false;
        }

        var save = await _configService.SaveStationAsync(_projectDirectory, station);
        if (!save.Succeeded)
        {
            _settingsPage.SetSaveResult(string.Join(Environment.NewLine, save.Errors), true);
            return false;
        }

        _yardRfidStationResolver = new YardRfidStationResolver(
            _loadedProject.Stations,
            _loadedProject.RfidStations);
        ApplyCurrentYardDisplayScope();
        _settingsPage.RefreshBindingOverview();
        _settingsPage.SetSaveResult("地图绑定已保存。", false);
        return true;
    }

    private void ViewRfidMapPoint(string yardId, string deviceId)
    {
        if (_loadedProject is null || _monitorPage is null)
        {
            return;
        }

        var yard = _loadedProject.Stations.FirstOrDefault(item =>
            string.Equals(item.Id, yardId, StringComparison.OrdinalIgnoreCase));
        if (yard is null)
        {
            return;
        }

        SelectNavigationButton(MonitorNavButton);
        _currentYardContext.SelectYard(yard.Id);
        ShowStation(yard.Id);
        _monitorPage.SelectRfidStationDevice(deviceId);
        PageContent.Content = _monitorPage;
    }

    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleWindowState();
            e.Handled = true;
            return;
        }

        if (WindowState == WindowState.Normal)
        {
            DragMove();
            e.Handled = true;
        }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object sender, RoutedEventArgs e) => ToggleWindowState();

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private async Task RecreateYardCommunicationManagerAsync(
        ProjectConfig project,
        IReadOnlyList<RfidStationConfig> stations,
        RfidSettings runtimeSettings)
    {
        _externalAlarmStatusChangeTracker.Clear();
        if (_yardCommunicationManager is not null)
        {
            foreach (var context in _yardCommunicationManager.Contexts.Values)
            {
                if (context.RuntimeCoordinator is not null)
                {
                    context.RuntimeCoordinator.AlarmRecovered -= OnAlarmRecovered;
                }
            }
            _yardCommunicationManager.Dispose();
            _yardCommunicationManager.DatagramReceived -= OnYardDatagramReceived;
            _yardCommunicationManager.DatagramSent -= OnYardDatagramSent;
            _yardCommunicationManager.ReceiveError -= OnYardReceiveError;
            _yardCommunicationManager.CommandSent -= OnYardCommandSent;
            _yardCommunicationManager.StationCommandSent -= OnYardStationCommandSent;
            _yardCommunicationManager.AlarmForwardFailed -= OnYardAlarmForwardFailed;
            _yardCommunicationManager.ExternalAlarmStatusSendFailed -= OnYardExternalAlarmStatusSendFailed;
            _yardCommunicationManager.ExternalAlarmStatusSent -= OnYardExternalAlarmStatusSent;
            _yardCommunicationManager.ExternalAlarmStatusUpdated -= OnYardExternalAlarmStatusUpdated;
            _yardCommunicationManager.ExternalAlarmFrameReceived -= OnYardExternalAlarmFrameReceived;
            _yardCommunicationManager.ExternalAlarmClearProcessed -= OnYardExternalAlarmClearProcessed;
            _yardCommunicationManager.ExternalAlarmFrameRejected -= OnYardExternalAlarmFrameRejected;
            _yardCommunicationManager.ExternalAlarmReceiveError -= OnYardExternalAlarmReceiveError;
        }
        _yardCommunicationManager = null;
        _acceptanceRuntimeStateWriter?.Dispose();
        _acceptanceRuntimeStateWriter = null;

        YardCommunicationManager manager;
        var externalAlarmStatusCoordinators = CreateExternalAlarmStatusCoordinators(project);
        _externalInterfaceAvailable = externalAlarmStatusCoordinators.Count > 0;
        if (_acceptanceOptions.Enabled)
        {
            manager = new YardCommunicationManager(
                CreateAcceptanceYardCommunications(),
                stations,
                runtimeSettings,
                _passageRecordStore,
                externalAlarmStatusCoordinators: externalAlarmStatusCoordinators,
                vehicleConfigurations: project.Vehicles,
                passageEventLogger: ((App)Application.Current).PassageEventLogger);
        }
        else if (project.UsesLegacySharedListener || project.YardCommunications.Count == 0)
        {
            var (listenAddress, listenPort) = ResolveLegacyListenerEndpoint();
            manager = YardCommunicationManager.CreateLegacyShared(
                stations,
                runtimeSettings,
                _passageRecordStore,
                listenAddress,
                listenPort,
                externalAlarmStatusCoordinators: externalAlarmStatusCoordinators,
                vehicleConfigurations: project.Vehicles,
                passageEventLogger: ((App)Application.Current).PassageEventLogger);
        }
        else
        {
            manager = new YardCommunicationManager(
                project.YardCommunications,
                stations,
                runtimeSettings,
                _passageRecordStore,
                externalAlarmStatusCoordinators: externalAlarmStatusCoordinators,
                vehicleConfigurations: project.Vehicles,
                passageEventLogger: ((App)Application.Current).PassageEventLogger);
        }

        foreach (var context in manager.Contexts.Values)
        {
            if (context.RuntimeCoordinator is not null)
            {
                context.RuntimeCoordinator.VehicleMatchResolved += LogVehicleMatch;
                context.RuntimeCoordinator.AlarmRecovered += OnAlarmRecovered;
            }
        }

        manager.DatagramReceived += OnYardDatagramReceived;
        manager.DatagramSent += OnYardDatagramSent;
        manager.ReceiveError += OnYardReceiveError;
        manager.CommandSent += OnYardCommandSent;
        manager.StationCommandSent += OnYardStationCommandSent;
        manager.AlarmForwardFailed += OnYardAlarmForwardFailed;
        manager.ExternalAlarmStatusSendFailed += OnYardExternalAlarmStatusSendFailed;
        manager.ExternalAlarmStatusSent += OnYardExternalAlarmStatusSent;
        manager.ExternalAlarmStatusUpdated += OnYardExternalAlarmStatusUpdated;
        manager.ExternalAlarmFrameReceived += OnYardExternalAlarmFrameReceived;
        manager.ExternalAlarmClearProcessed += OnYardExternalAlarmClearProcessed;
        manager.ExternalAlarmFrameRejected += OnYardExternalAlarmFrameRejected;
        manager.ExternalAlarmReceiveError += OnYardExternalAlarmReceiveError;
        _yardCommunicationManager = manager;

        var pendingClearRecords = _passageRecordStore.GetPendingClear();
        var unacknowledgedAlarms = _passageRecordStore.GetUnacknowledgedAlarms();
        foreach (var context in manager.Contexts.Values)
        {
            context.RestorePendingClear(pendingClearRecords);
            context.RestoreUnacknowledgedAlarms(unacknowledgedAlarms);
        }

        if (_acceptanceOptions.Enabled)
        {
            var runtimeCoordinators = manager.Contexts.Values
                .Select(context => context.RuntimeCoordinator)
                .Where(coordinator => coordinator is not null)
                .Cast<RfidRuntimeCoordinator>()
                .ToArray();
            if (runtimeCoordinators.Length > 0)
            {
                _acceptanceRuntimeStateWriter = new AcceptanceRuntimeStateWriter(
                    _acceptanceOptions.RuntimeStatePath!,
                    runtimeCoordinators);
            }
        }

        await manager.StartAllAsync();
        LogYardEndpointDiagnostics(manager, project);
        _rfidListenerHealthy = IsCommunicationHealthy();
        UpdateHeaderStatusIndicators();
        _communicationPage.SetListenerStatus(
            manager.Contexts.Count == 0
                ? "未创建 RFID 站场通信上下文"
                : $"通信上下文：{manager.Contexts.Count} 个，运行中：{manager.Contexts.Values.Count(context => context.IsRunning)} 个");
        foreach (var diagnostic in manager.Diagnostics)
        {
            ((App)Application.Current).Logger.Warning(diagnostic);
        }
    }

    private static void LogYardEndpointDiagnostics(
        YardCommunicationManager manager,
        ProjectConfig project)
    {
        foreach (var context in manager.Contexts.Values.OrderBy(
                     item => item.Configuration.YardId,
                     StringComparer.OrdinalIgnoreCase))
        {
            var configuration = context.Configuration;
            var rfidEndpoint = configuration.TryResolveEndpoint(out var resolvedRfidEndpoint)
                ? resolvedRfidEndpoint.ToString()
                : "未配置";
            var externalLocalEndpoint = configuration.TryResolveExternalAlarmEndpoint(out var resolvedExternalEndpoint)
                ? resolvedExternalEndpoint.ToString()
                : "未配置";
            var targetConfiguration = project.YardAlarmForwards.FirstOrDefault(item =>
                string.Equals(item.YardId, context.Configuration.YardId, StringComparison.OrdinalIgnoreCase));
            var externalTargetEndpoint = targetConfiguration is not null &&
                targetConfiguration.TryResolveEndpoint(out var resolvedTargetEndpoint)
                ? resolvedTargetEndpoint.ToString()
                : "未配置";

            ((App)Application.Current).Logger.Information(
                $"Yard={context.Configuration.YardId} " +
                $"RFID监听端点={rfidEndpoint} " +
                $"External本机端点={externalLocalEndpoint} " +
                $"External目标端点={externalTargetEndpoint}");
        }
    }

    private (IPAddress Address, int Port) ResolveLegacyListenerEndpoint()
    {
        if (_acceptanceOptions.Enabled)
        {
            return (_acceptanceOptions.ListenAddress, _acceptanceOptions.ListenPort);
        }

        var addressText = ConfigurationManager.AppSettings["RfidUdpListenAddress"];
        var portText = ConfigurationManager.AppSettings["RfidUdpListenPort"];
        if (!IPAddress.TryParse(addressText, out var address) || address == IPAddress.None ||
            !int.TryParse(portText, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
        {
            throw new InvalidOperationException("RFID UDP 监听配置无效。");
        }

        return (address, port);
    }

    private void OnYardDatagramReceived(YardCommunicationContext context, RfidUdpDatagramEventArgs args)
    {
        _rawPacketBlackBoxWriter.TryEnqueue(CreateRxBlackBoxRecord(context, args));
        if (!context.IsRunning)
        {
            return;
        }

        var stationId = ResolveRfidStationId(context, args);
        string? hex = null;
        if (_verboseRfidFrameLoggingEnabled)
        {
            hex = BitConverter.ToString(args.Data).Replace('-', ' ');
            var stationMatch = stationId is null ? "Unmatched" : $"Matched:{stationId}";
            var byte2 = args.Data.Length >= 3 ? $"0x{args.Data[2]:X2}" : "N/A";
            var byte7 = args.Data.Length >= 8 ? args.Data[7].ToString() : "N/A";
            ((App)Application.Current).Logger.Information(
                $"RFID RX DIAG Time={args.ReceivedAt:O} SourceIP={args.RemoteEndPoint.Address} " +
                $"SourcePort={args.RemoteEndPoint.Port} Byte2={byte2} Byte7={byte7} " +
                $"StationIdMatch={stationMatch} HEX={hex}");
        }

        var responseMatched = false;
        if (args.IsValid)
        {
            if (args.Data.Length >= 3)
            {
                responseMatched = context.RecordResponse(args.RemoteEndPoint, args.Data[2], args.ReceivedAt);
            }
            if (responseMatched)
            {
                if (_verboseRfidFrameLoggingEnabled)
                {
                    hex ??= BitConverter.ToString(args.Data).Replace('-', ' ');
                    ((App)Application.Current).Logger.Information(
                        $"RFID UDP RX [{context.YardId}] {args.RemoteEndPoint} {args.Data.Length} Bytes {hex}");
                }
            }
            else
            {
                hex ??= BitConverter.ToString(args.Data).Replace('-', ' ');
                var message = $"RFID UDP RX [{context.YardId}] {args.RemoteEndPoint} {args.Data.Length} Bytes {hex}";
                ((App)Application.Current).Logger.Warning($"未匹配 RFID 基站端点或协议地址，已丢弃：{message}");
            }
        }
        else
        {
            hex ??= BitConverter.ToString(args.Data).Replace('-', ' ');
            var message = $"RFID UDP RX [{context.YardId}] {args.RemoteEndPoint} {args.Data.Length} Bytes {hex}";
            ((App)Application.Current).Logger.Warning($"非法 RFID UDP 帧：{args.ValidationError}；{message}");
        }

        RfidStationFrame? parsedFrame = null;
        if (responseMatched && args.Data.Length >= 4 && args.Data[3] == 0x04)
        {
            _rfidFrameParser.TryParse(args.Data, args.RemoteEndPoint, args.ReceivedAt, out parsedFrame);
        }

        if (_verboseRfidFrameLoggingEnabled)
        {
            ((App)Application.Current).Logger.Information(
                RfidReceiveDiagnosticFormatter.Format(
                    context.YardId,
                    stationId,
                    args,
                    parsedFrame));
        }

        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            var recognitionSession = (StationRecognitionSession?)null;
            if (parsedFrame is not null)
            {
                var runtimeStateBefore = string.IsNullOrWhiteSpace(stationId)
                    ? null
                    : context.RuntimeStates.FirstOrDefault(state => string.Equals(
                        state.StationId,
                        stationId,
                        StringComparison.OrdinalIgnoreCase));
                var previousLifecycle = runtimeStateBefore?.LifecycleState ?? PassageLifecycleState.Idle;
                var previousEmptyReads = runtimeStateBefore?.ConsecutiveEmptyReads ?? 0;
                var previousPassageId = runtimeStateBefore?.RecognitionSession?.PassageId ?? runtimeStateBefore?.LastPassageRecord?.PassageId;
                var isEmptyFrame = parsedFrame.ValidRfids.Count == 0;
                recognitionSession = context.ProcessFrame(parsedFrame);
                var runtimeState = stationId is null
                    ? null
                    : context.RuntimeStates.FirstOrDefault(state => string.Equals(
                        state.StationId,
                        stationId,
                        StringComparison.OrdinalIgnoreCase));
                if (runtimeState is not null)
                {
                    if (!isEmptyFrame && previousLifecycle == PassageLifecycleState.Idle && runtimeState.RecognitionSession is not null)
                    {
                        LogRfidLifecycleDiagnostic(
                            "NEXT_VEHICLE_FIRST_NON_EMPTY_FRAME",
                            runtimeState,
                            args.ReceivedAt,
                            detail: "first-non-empty-frame");
                    }
                    else if (!isEmptyFrame && previousLifecycle is PassageLifecycleState.Completed or
                             PassageLifecycleState.Alarm or PassageLifecycleState.Clearing or PassageLifecycleState.WaitForEmpty)
                    {
                        LogRfidLifecycleDiagnostic(
                            "NON_EMPTY_FRAME_AFTER_TERMINAL_STATE",
                            runtimeState,
                            args.ReceivedAt,
                            detail: $"PreviousState={previousLifecycle};CandidateMayBeResidualOrNextTrain");
                    }

                    if (runtimeState.LifecycleState != previousLifecycle)
                    {
                        LogRfidLifecycleDiagnostic(
                            "STATE_TRANSITION",
                            runtimeState,
                            args.ReceivedAt,
                            detail: $"From={previousLifecycle};To={runtimeState.LifecycleState}",
                            passageIdOverride: previousPassageId);
                    }

                    var emptyConfirmationReads = RfidRuntimePolicy.Default.EmptyConfirmationReads;
                    if (isEmptyFrame &&
                        runtimeState.LifecycleState == PassageLifecycleState.WaitForEmpty &&
                        runtimeState.ConsecutiveEmptyReads > previousEmptyReads)
                    {
                        LogRfidLifecycleDiagnostic(
                            "EMPTY_CONFIRMATION",
                            runtimeState,
                            args.ReceivedAt,
                            runtimeState.ConsecutiveEmptyReads,
                            $"Count={runtimeState.ConsecutiveEmptyReads}/{emptyConfirmationReads}");
                    }
                    else if (isEmptyFrame &&
                             runtimeState.LifecycleState == PassageLifecycleState.Idle &&
                             previousLifecycle == PassageLifecycleState.WaitForEmpty &&
                             previousEmptyReads > 0)
                    {
                        var confirmedCount = Math.Min(previousEmptyReads + 1, emptyConfirmationReads);
                        LogRfidLifecycleDiagnostic(
                            "EMPTY_CONFIRMATION",
                            runtimeState,
                            args.ReceivedAt,
                            confirmedCount,
                            $"Count={confirmedCount}/{emptyConfirmationReads}",
                            previousPassageId);
                    }
                    else if (!isEmptyFrame &&
                             runtimeState.LifecycleState == PassageLifecycleState.WaitForEmpty &&
                             previousEmptyReads > 0)
                    {
                        LogRfidLifecycleDiagnostic(
                            "EMPTY_CONFIRMATION_RESET",
                            runtimeState,
                            args.ReceivedAt,
                            0,
                            $"PreviousCount={previousEmptyReads}/{emptyConfirmationReads};NonEmptyFrame",
                            previousPassageId);
                    }
                }
                if (_verboseRfidFrameLoggingEnabled)
                {
                    ((App)Application.Current).Logger.Information(
                        $"[RFIDRuntime] Time={args.ReceivedAt:O} Yard={context.YardId} " +
                        $"RuntimeKey={args.RemoteEndPoint}/0x{parsedFrame.StationAddress:X2} " +
                        $"StationId={runtimeState?.StationId ?? stationId ?? "Unmatched"} " +
                        $"ProtocolAddress(Byte2)=0x{parsedFrame.StationAddress:X2} " +
                        $"VehicleCount={runtimeState?.DetectedVehicleCount.ToString() ?? "N/A"} " +
                        $"ExpectedCount={runtimeState?.ExpectedVehicleCount.ToString() ?? "N/A"} " +
                        $"PassageId={(runtimeState?.RecognitionSession?.PassageId ?? runtimeState?.LastPassageRecord?.PassageId)?.ToString("D") ?? "-"} " +
                        $"LastNewVehicleAt={runtimeState?.LastNewVehicleAt?.ToString("O") ?? "-"} " +
                        $"LifecycleState={runtimeState?.LifecycleState.ToString() ?? "Unmatched"}");
                }
                if (recognitionSession is not null)
                {
                    _monitorPage?.SetRecognitionSnapshot(parsedFrame, recognitionSession);
                }
                UpdateRecognitionStatus();
                UpdateRfidRuntimeUi();
                _acceptanceRuntimeStateWriter?.Write("frame");
            }
            _communicationPage.AddDatagram(context.YardId, args, parsedFrame, recognitionSession);
            if (responseMatched)
            {
                _rfidListenerHealthy = IsCommunicationHealthy();
                UpdateHeaderStatusIndicators();
                _monitorPage?.SetSystemRfidStatus(true);
                _communicationPage.SetListenerStatus($"[{context.YardId}] 最近收到有效报文：{context.ListenerEndPoint}", context.YardId);
            }
        }));
    }

    private void LogVehicleMatch(VehicleMatchResult result)
    {
        ((App)Application.Current).Logger.Information(
            VehicleMatchDiagnosticFormatter.Format(result));
    }

    private static void LogRfidLifecycleDiagnostic(
        string eventType,
        StationRuntimeState state,
        DateTimeOffset occurredAt,
        int? emptyConfirmationCount = null,
        string? detail = null,
        Guid? passageIdOverride = null)
    {
        var passageId = passageIdOverride ?? state.RecognitionSession?.PassageId ?? state.LastPassageRecord?.PassageId;
        ((App)Application.Current).Logger.Information(
            RfidRuntimeDiagnosticFormatter.FormatLifecycle(
                state.StationId,
                passageId,
                occurredAt,
                eventType,
                state.LifecycleState,
                state.ExpectedVehicleCount,
                state.DetectedVehicleCount,
                state.LastNewVehicleAt,
                state.ClearAttempts,
                emptyConfirmationCount ?? state.ConsecutiveEmptyReads,
                RfidRuntimePolicy.Default.EmptyConfirmationReads,
                detail));
    }

    private static string? ResolveRfidStationId(
        YardCommunicationContext context,
        RfidUdpDatagramEventArgs args)
    {
        if (args.Data.Length < 3)
        {
            return null;
        }

        var protocolAddress = args.Data[2];
        foreach (var station in context.Stations)
        {
            if (station.ProtocolAddress != protocolAddress ||
                !station.TryResolveEndpoint(out var endpoint) ||
                !endpoint.Equals(args.RemoteEndPoint))
            {
                continue;
            }

            return station.StationId;
        }

        return null;
    }

    private void OnYardDatagramSent(YardCommunicationContext context, RfidUdpDatagramSentEventArgs args)
    {
        _rawPacketBlackBoxWriter.TryEnqueue(CreateTxBlackBoxRecord(context, args));
    }

    private RawPacketBlackBoxRecord CreateRxBlackBoxRecord(
        YardCommunicationContext context,
        RfidUdpDatagramEventArgs args)
        => RawPacketBlackBoxRecordFactory.FromReceived(context, args);

    private RawPacketBlackBoxRecord CreateTxBlackBoxRecord(
        YardCommunicationContext context,
        RfidUdpDatagramSentEventArgs args)
        => RawPacketBlackBoxRecordFactory.FromSent(context, args);

    private void OnOpenBlackBoxDirectoryRequested()
    {
        try
        {
            Directory.CreateDirectory(_rawPacketBlackBoxWriter.RootDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{_rawPacketBlackBoxWriter.RootDirectory}\"",
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            ((App)Application.Current).Logger.Error("打开 RFID 原始报文黑匣子目录失败。", exception);
            _communicationPage.SetError(exception, null);
        }
    }

    private void OnYardReceiveError(YardCommunicationContext context, Exception exception)
    {
        ((App)Application.Current).Logger.Error($"RFID UDP 接收异常 [{context.YardId}]。", exception);
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            _rfidListenerHealthy = IsCommunicationHealthy();
            UpdateHeaderStatusIndicators();
            _monitorPage?.SetSystemRfidStatus(false);
            _communicationPage.SetError(exception, context.YardId);
        }));
    }

    private void OnYardAlarmForwardFailed(
        YardCommunicationContext context,
        AlarmForwardRequest request,
        Exception exception)
    {
        var target = _loadedProject?.YardAlarmForwards
            .FirstOrDefault(item => string.Equals(item.YardId, context.YardId, StringComparison.OrdinalIgnoreCase));
        var targetText = target is null ? "未配置" : $"{target.TargetIp}:{target.TargetPort}";
        ((App)Application.Current).Logger.Error(
            $"报警 UDP 转发失败 Yard={context.YardId} StationId={request.StationId} " +
            $"PassageId={request.PassageId} Target={targetText} " +
            $"Timestamp={request.RequestedAt:O} Error={exception.Message}",
            exception);
    }

    private void OnYardExternalAlarmStatusSendFailed(
        YardCommunicationContext context,
        ExternalAlarmStationSnapshot snapshot,
        Exception exception)
    {
        var target = _loadedProject?.YardAlarmForwards
            .FirstOrDefault(item => string.Equals(item.YardId, context.YardId, StringComparison.OrdinalIgnoreCase));
        var targetText = target is null ? "未配置" : $"{target.TargetIp}:{target.TargetPort}";
        ((App)Application.Current).Logger.Error(
            $"外部报警状态 UDP 发送失败 Yard={context.YardId} " +
            $"StationAddress=0x{snapshot.StationAddress:X2} Status={snapshot.Status} " +
            $"VehicleCount={snapshot.AccumulatedVehicleCount} Target={targetText} " +
            $"Error={exception.Message}",
            exception);
        _ = Dispatcher.BeginInvoke(new Action(() =>
            _communicationPage.AddExternalAlarmSendFailure(context.YardId, snapshot, exception)));
    }

    private void OnYardExternalAlarmClearProcessed(
        YardCommunicationContext context,
        ExternalAlarmClearCommand command,
        IPEndPoint sourceEndpoint,
        bool processed)
    {
        var message =
            $"外部报警清除命令 Yard={context.YardId} " +
            $"StationAddress=0x{command.StationAddress:X2} " +
            $"Source={sourceEndpoint} " +
            (processed ? "已进入现有清除流程。" : "未找到对应待处理报警，已忽略。");
        if (processed)
        {
            ((App)Application.Current).Logger.Information(message);
        }
        else
        {
            ((App)Application.Current).Logger.Warning(message);
        }
    }

    private void OnYardExternalAlarmStatusSent(
        YardCommunicationContext context,
        ExternalAlarmStationSnapshot snapshot,
        byte[] payload)
    {
        var stationKey = $"{context.YardId}:{snapshot.StationId}";
        if (!_externalAlarmStatusChangeTracker.TryRecord(
                stationKey,
                snapshot.Status,
                out var previousStatus))
        {
            return;
        }

        var previous = previousStatus.HasValue
            ? $"0x{(byte)previousStatus.Value:X2}({previousStatus.Value})"
            : "首次发送";
        var current = $"0x{(byte)snapshot.Status:X2}({snapshot.Status})";
        ((App)Application.Current).Logger.Information(
            $"外部报警状态变化 Yard={context.YardId} " +
            $"StationAddress=0x{snapshot.StationAddress:X2} " +
            $"{previous}->{current} " +
            $"车厢数={snapshot.AccumulatedVehicleCount}");

        _ = Dispatcher.BeginInvoke(new Action(() =>
            _communicationPage.AddExternalAlarmSent(
                context.YardId,
                snapshot,
                payload,
                DateTimeOffset.Now)));
    }

    private void OnYardExternalAlarmStatusUpdated(
        YardCommunicationContext context,
        ExternalAlarmStationSnapshot snapshot)
    {
        ((App)Application.Current).Logger.Information(
            $"[ExternalUpdate] Yard={context.YardId} StationId={snapshot.StationId} " +
            $"Address=0x{snapshot.StationAddress:X2} Status={snapshot.Status} " +
            $"VehicleCount={snapshot.AccumulatedVehicleCount}");
    }

    private void OnYardExternalAlarmFrameReceived(
        YardCommunicationContext context,
        byte[] payload,
        IPEndPoint sourceEndpoint,
        bool isValid,
        DateTimeOffset receivedAt) =>
        _ = Dispatcher.BeginInvoke(new Action(() =>
            _communicationPage.AddExternalAlarmReceived(
                context.YardId,
                payload,
                sourceEndpoint,
                isValid,
                receivedAt)));

    private void OnYardExternalAlarmFrameRejected(
        YardCommunicationContext context,
        string reason,
        IPEndPoint sourceEndpoint)
    {
        ((App)Application.Current).Logger.Warning(
            $"外部报警 UDP 清除帧已丢弃 Yard={context.YardId} Source={sourceEndpoint} Reason={reason}");
        _ = Dispatcher.BeginInvoke(new Action(() =>
            _communicationPage.AddExternalAlarmFrameRejected(context.YardId, reason, sourceEndpoint)));
    }

    private void OnYardExternalAlarmReceiveError(
        YardCommunicationContext context,
        Exception exception)
    {
        ((App)Application.Current).Logger.Error(
            $"外部报警 UDP 接收异常 [{context.YardId}]。", exception);
        _ = Dispatcher.BeginInvoke(new Action(() =>
            _communicationPage.AddExternalAlarmReceiveError(context.YardId, exception)));
    }

    private async Task StopRuntimeThenCloseAsync()
    {
        try
        {
            var app = (App)Application.Current;
            _clockTimer.Stop();

            var manager = _yardCommunicationManager;
            if (manager is not null)
            {
                await manager.StopAllAsync();
                manager.Dispose();
                manager.DatagramReceived -= OnYardDatagramReceived;
                manager.DatagramSent -= OnYardDatagramSent;
                manager.ReceiveError -= OnYardReceiveError;
                manager.CommandSent -= OnYardCommandSent;
                manager.StationCommandSent -= OnYardStationCommandSent;
                manager.AlarmForwardFailed -= OnYardAlarmForwardFailed;
                manager.ExternalAlarmStatusSendFailed -= OnYardExternalAlarmStatusSendFailed;
                manager.ExternalAlarmStatusSent -= OnYardExternalAlarmStatusSent;
                manager.ExternalAlarmStatusUpdated -= OnYardExternalAlarmStatusUpdated;
                manager.ExternalAlarmFrameReceived -= OnYardExternalAlarmFrameReceived;
                manager.ExternalAlarmClearProcessed -= OnYardExternalAlarmClearProcessed;
                manager.ExternalAlarmFrameRejected -= OnYardExternalAlarmFrameRejected;
                manager.ExternalAlarmReceiveError -= OnYardExternalAlarmReceiveError;
                _yardCommunicationManager = null;
            }

            if (_acceptanceRuntimeStateWriter is not null)
            {
                try
                {
                    _acceptanceRuntimeStateWriter.Write("closed");
                }
                catch (Exception exception)
                {
                    app.Logger.Error("写入验收运行时最终快照失败。", exception);
                }

                _acceptanceRuntimeStateWriter.Dispose();
                _acceptanceRuntimeStateWriter = null;
            }

            _rawPacketBlackBoxWriter.Dispose();
            await app.StopDatabaseInfrastructureAsync();

            _allowWindowClose = true;
            _closeInProgress = false;
            Close();
        }
        catch (Exception exception)
        {
            ((App)Application.Current).Logger.Error("程序安全关闭失败。", exception);
            _closeInProgress = false;
            var dialog = new StyledMessageDialog(
                "程序安全关闭失败",
                "程序安全关闭失败，请重试退出。",
                MessageDialogKind.Error)
            {
                Owner = this
            };
            dialog.ShowDialog();
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _adminModeService.PropertyChanged -= OnAdminModeStateChanged;
        _clockTimer.Stop();
    }

    private void OnYardCommandSent(
        YardCommunicationContext context,
        byte stationAddress,
        RfidPollCommand command,
        DateTimeOffset sentAt)
    {
        if (command == RfidPollCommand.Clear)
        {
        }
    }

    private void OnYardStationCommandSent(
        YardCommunicationContext context,
        RfidStationConfig station,
        RfidPollCommand command,
        DateTimeOffset sentAt)
    {
        if (command == RfidPollCommand.Clear)
        {
            var state = context.RuntimeStates.FirstOrDefault(item => string.Equals(
                item.StationId,
                station.StationId,
                StringComparison.OrdinalIgnoreCase));
            if (state is not null)
            {
                LogRfidLifecycleDiagnostic(
                    "CLEAR_COMMAND_SENT",
                    state,
                    sentAt,
                    detail: $"Attempt={state.ClearAttempts};Retry={state.ClearAttempts > 1}");
            }
            _acceptanceRuntimeStateWriter?.Write("clear-command-sent");
        }

        _ = Dispatcher.BeginInvoke(new Action(() =>
            _communicationPage.AddCommandSent(station, command, sentAt)));
    }

    private void OnAdminModeStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AdminModeService.IsAdmin))
        {
            RecordOperationChanges(new[]
            {
                new OperationRecord
                {
                    OperationType = _adminModeService.IsAdmin ? "ADMIN_LOGIN" : "ADMIN_LOGOUT",
                    TargetType = "管理员模式",
                    TargetName = "管理员",
                    BeforeValue = _adminModeService.IsAdmin ? "状态: 未登录" : "状态: 已登录",
                    AfterValue = _adminModeService.IsAdmin ? "状态: 已登录" : "状态: 未登录",
                    Result = "成功"
                }
            });
            UpdateAdminModeBanner();
            _monitorPage?.HandleAdminModeChanged(_adminModeService.IsAdmin);
            _communicationPage.SetAdminMode(_adminModeService.IsAdmin);
            _runLogPage.SetAdminMode(_adminModeService.IsAdmin);
        }
    }

    private void UpdateAdminModeBanner()
    {
        var visibility = _adminModeService.IsAdmin ? Visibility.Visible : Visibility.Collapsed;
        AdminModeBanner.Visibility = visibility;
        NormalStatusPanel.Visibility = _adminModeService.IsAdmin ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void OnExitAdminModeClick(object sender, RoutedEventArgs e)
    {
        await ExitAdminModeAsync();
    }

    private async Task<bool> ExitAdminModeAsync()
    {
        if (_monitorPage is not null && !await _monitorPage.TryLeaveMapEditingAsync("退出管理员模式"))
        {
            return false;
        }

        if (_settingsPage is not null && !await _settingsPage.TryLeaveAsync("退出管理员模式"))
        {
            return false;
        }

        _adminModeService.ExitAdminMode();
        return true;
    }

    private async void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowWindowClose)
        {
            return;
        }

        e.Cancel = true;
        if (_closeInProgress)
        {
            return;
        }

        _closeInProgress = true;
        try
        {
            if (_monitorPage is not null && _monitorPage.HasUnsavedMapChanges)
            {
                if (!await _monitorPage.TryLeaveMapEditingAsync("关闭程序"))
                {
                    _closeInProgress = false;
                    return;
                }
            }

            if (_settingsPage is not null && _settingsPage.HasUnsavedChanges)
            {
                if (!await _settingsPage.TryLeaveAsync("关闭程序"))
                {
                    _closeInProgress = false;
                    return;
                }
            }

            await StopRuntimeThenCloseAsync();
        }
        catch (Exception exception)
        {
            ((App)Application.Current).Logger.Error("程序安全关闭失败。", exception);
            _closeInProgress = false;
            var dialog = new StyledMessageDialog(
                "程序安全关闭失败",
                "程序安全关闭失败，请重试退出。",
                MessageDialogKind.Error)
            {
                Owner = this
            };
            dialog.ShowDialog();
        }
    }

    private static ushort ReadEmptyRfidValue()
    {
        var value = ConfigurationManager.AppSettings["EmptyRfidValue"];
        return ushort.TryParse(value, out var parsedValue) ? parsedValue : (ushort)0;
    }

    private void ApplyDwmBorderFallback()
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT || Environment.OSVersion.Version.Build < 22000)
        {
            return;
        }

        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        const int borderColorAttribute = 34; // DWMWA_BORDER_COLOR, Windows 11+
        var backgroundColor = 0x001F1103u; // #03111F as COLORREF (BGR)
        _ = DwmSetWindowAttribute(handle, borderColorAttribute, ref backgroundColor, sizeof(uint));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref uint value, int valueSize);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point Reserved;
        public Point MaxSize;
        public Point MaxPosition;
        public Point MinTrackSize;
        public Point MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect rcMonitor;
        public Rect rcWork;
        public uint Flags;
    }

    private void ToggleWindowState()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private async void OnStationClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string stationId && _loadedProject is not null && _monitorPage is not null)
        {
            if (!await _monitorPage.TryLeaveMapEditingAsync("切换站场"))
            {
                return;
            }

            if (_settingsPage is not null && !await _settingsPage.TryLeaveAsync("切换站场"))
            {
                return;
            }

            SelectNavigationButton(MonitorNavButton);
            SelectStationButton(button);
            _currentYardContext.SelectYard(stationId);
            ShowStation(stationId);
            PageContent.Content = _monitorPage;
        }
    }

    private void ShowStation(string stationId)
    {
        if (_loadedProject is null || _monitorPage is null)
        {
            return;
        }

        var station = _loadedProject.Stations.FirstOrDefault(item => item.Id.Equals(stationId, StringComparison.OrdinalIgnoreCase));
        if (station is null)
        {
            return;
        }

        var image = LoadImage(Path.Combine(_projectDirectory, station.BackgroundImage));
        StationPreviewImage.Source = image;
        _monitorPage.SetStation(station, image);

        var stationButton = StationButtonsPanel.Children
            .OfType<Button>()
            .FirstOrDefault(button => string.Equals(button.Tag as string, station.Id, StringComparison.OrdinalIgnoreCase));
        SelectStationButton(stationButton);
    }

    private BitmapImage? LoadImage(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                ((App)Application.Current).Logger.Warning($"站场底图不存在：{path}");
                return null;
            }

            using var stream = File.OpenRead(path);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception exception)
        {
            ((App)Application.Current).Logger.Error($"站场底图加载失败：{path}", exception);
            return null;
        }
    }

    private void OnClockTick(object? sender, EventArgs e)
    {
        UpdateClock();
        var lifecycleBeforeEvaluation = GetAllRuntimeStates()
            .ToDictionary(state => state, state => state.LifecycleState);
        var evaluatedAt = DateTimeOffset.Now;
        _yardCommunicationManager?.Evaluate(evaluatedAt);
        foreach (var state in GetAllRuntimeStates())
        {
            if (lifecycleBeforeEvaluation.TryGetValue(state, out var previousLifecycle) &&
                state.LifecycleState != previousLifecycle)
            {
                LogRfidLifecycleDiagnostic(
                    "STATE_TRANSITION",
                    state,
                    evaluatedAt,
                    detail: $"From={previousLifecycle};To={state.LifecycleState};Source=Evaluate");
            }
        }
        _communicationPage.SetBlackBoxStatus(_rawPacketBlackBoxWriter.GetSnapshot());
        _acceptanceRuntimeStateWriter?.Write("tick");
        UpdateRecognitionStatus();
        UpdateRfidRuntimeUi();
        if (DateTimeOffset.Now - _lastStatisticsRefresh >= TimeSpan.FromSeconds(10))
        {
            RefreshHistoricalStatistics();
        }

        if (_acceptanceOptions.Enabled && File.Exists(_acceptanceOptions.StopFile!))
        {
            _acceptanceRuntimeStateWriter?.Write("stop");
            Application.Current.Shutdown();
        }
    }

    private void UpdateRecognitionStatus()
    {
        // The canonical head-warning text is produced by RfidRuntimeCoordinator:
        // 未检测到车头槽位(Position 0)、首个识别RFID未出现在车头槽位(Position 0)、检测到多个车头槽位RFID、协议数据告警。
        if (_monitorPage is null)
        {
            return;
        }

        var states = FilterCurrentYardStates(GetAllRuntimeStates()).ToArray();
        var alarm = states.FirstOrDefault(state =>
            state.VisualState == RfidStationVisualState.Alarm ||
            state.LifecycleState == PassageLifecycleState.Alarm ||
            state.LastPassageRecord?.Outcome == PassageOutcome.UncouplingAlarm ||
            !string.IsNullOrWhiteSpace(state.AlarmMessage));
        if (alarm is not null)
        {
            _monitorPage.SetRecognitionStatus(
                $"脱节报警 · 基站 {alarm.StationAddress:X2} · 已识别 {alarm.DetectedVehicleCount}/{alarm.ExpectedVehicleCount} 节",
                isAlarm: true);
            return;
        }

        var offline = states.FirstOrDefault(state => state.CommunicationState == StationCommunicationState.Offline);
        if (offline is not null)
        {
            _monitorPage.SetRecognitionStatus($"基站 {offline.StationAddress:X2}：通信离线", isAlarm: false);
            return;
        }

        var warning = states.FirstOrDefault(state => state.WarningMessages.Count > 0);
        if (warning is not null)
        {
            _monitorPage.SetRecognitionStatus(
                $"基站 {warning.StationAddress:X2}：{string.Join("；", warning.WarningMessages)}",
                isAlarm: false);
            return;
        }

        _monitorPage.SetRecognitionStatus(null, isAlarm: false);
    }

    private void UpdateRfidRuntimeUi()
    {
        _communicationPage.SetExternalAlarmStationStates(GetExternalAlarmDiagnostics());
        if (_monitorPage is null)
        {
            UpdateHeaderStatusIndicators();
            return;
        }

        var allRuntimeStates = GetAllRuntimeStates();
        _monitorPage.SetRfidRuntimeStates(FilterCurrentYardStates(allRuntimeStates));
        if (DateTimeOffset.Now - _lastRecentAlarmRefresh >= TimeSpan.FromSeconds(2))
        {
            RefreshRecentAlarmHistory();
        }
        _monitorPage.SetRfidPollingInfo(
            _loadedProject?.RfidSettings.PollIntervalMs ?? 200,
            FilterCurrentYardPollingStatuses(GetAllPollingStatuses()));
        _communicationPage.SetStationStatuses(
            GetAllPollingStatuses());
        if (ReferenceEquals(PageContent.Content, _runLogPage))
        {
            _runLogPage.RefreshStationStatuses();
        }
        _monitorPage.SetSystemRfidStatus(_rfidListenerHealthy);
        UpdateHeaderStatusIndicators();
    }

    private async Task SendCommunicationTestAsync(RfidStationConfig station, RfidPollCommand command)
    {
        if (!_adminModeService.IsAdmin)
        {
            var message = command == RfidPollCommand.Clear
                ? "发送清空命令需要管理员模式。"
                : "通信测试需要管理员模式。";
            throw new UnauthorizedAccessException(message);
        }

        var context = _yardCommunicationManager?.FindContextForStation(station);
        if (context is null)
        {
            throw new InvalidOperationException($"未找到基站所属的站场通信上下文：{station.StationId}。");
        }

        await context.SendAsync(station, command, CancellationToken.None);
        if (command == RfidPollCommand.Clear)
        {
            RecordOperationChanges(new[]
            {
                new OperationRecord
                {
                    OperationType = "发送基站清空命令",
                    TargetType = "基站",
                    TargetName = !string.IsNullOrWhiteSpace(station.Name) ? station.Name : station.StationId,
                    AfterValue = "操作: 已发送清空命令",
                    Result = "成功"
                }
            });
        }
    }

    private async Task<string?> CreateManualBackupAsync()
    {
        if (!_adminModeService.IsAdmin)
        {
            throw new UnauthorizedAccessException("数据库备份需要管理员模式。");
        }

        try
        {
            var app = (App)Application.Current;
            var databasePath = _acceptanceOptions.Enabled
                ? _acceptanceOptions.DatabasePath!
                : app.Paths.DatabasePath;
            var backupRoot = _acceptanceOptions.Enabled
                ? Path.Combine(_acceptanceOptions.LogDirectory!, "Backups", "SQLite")
                : app.Paths.SqliteBackupDirectory;
            var healthChecker = new SqliteDatabaseHealthChecker(new SystemRfidTimeProvider(), app.Logger);
            var backupService = new SqliteBackupService(healthChecker, app.Logger);
            var result = await Task.Run(() => backupService.CreateValidatedBackupAsync(
                databasePath,
                backupRoot,
                DateTimeOffset.Now,
                CancellationToken.None).GetAwaiter().GetResult());
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(result.ErrorMessage ?? "数据库备份失败。");
            }

            RecordOperationChanges(new[]
            {
                new OperationRecord
                {
                    OperationType = "DATABASE_BACKUP",
                    TargetType = "数据库",
                    TargetName = "SQLite",
                    AfterValue = $"备份文件: {Path.GetFileName(result.FinalPath)}",
                    Result = "成功"
                }
            });
            return result.FinalPath;
        }
        catch (Exception exception)
        {
            RecordOperationChanges(new[]
            {
                new OperationRecord
                {
                    OperationType = "DATABASE_BACKUP_FAILED",
                    TargetType = "数据库",
                    TargetName = "SQLite",
                    AfterValue = $"错误: {exception.Message}",
                    Result = "失败"
                }
            });
            throw;
        }
    }

    private void AcknowledgeAlarm(Guid passageId, DateTimeOffset acknowledgedAt)
    {
        var record = _passageRecordStore.GetDetails(passageId);
        if (record is null)
        {
            throw new InvalidOperationException($"未找到待确认报警记录：{passageId}。");
        }

        if (record.Outcome != PassageOutcome.UncouplingAlarm)
        {
            throw new InvalidOperationException($"普通告警不可确认：{passageId}。");
        }

        if (record.AlarmAcknowledgedAt.HasValue)
        {
            return;
        }

        var contexts = _yardCommunicationManager?.Contexts.Values
            .Where(context => context.HasUnacknowledgedAlarm(passageId))
            .ToArray() ?? Array.Empty<YardCommunicationContext>();
        if (contexts.Length > 1)
        {
            throw new InvalidOperationException($"未找到唯一的待确认报警：{passageId}。");
        }

        if (contexts.Length == 1)
        {
            contexts[0].AcknowledgeAlarm(passageId, acknowledgedAt);
        }
        else
        {
            _passageRecordStore.MarkAlarmAcknowledged(passageId, acknowledgedAt);
        }

        RecordOperationChanges(new[] { AlarmOperationRecordFactory.CreateAcknowledged(record, acknowledgedAt) });
        var updatedRecord = _passageRecordStore.GetDetails(passageId);
        if (contexts.Length == 0 && record.AlarmRecoveredAt is null && updatedRecord?.AlarmRecoveredAt.HasValue == true)
        {
            OnAlarmRecovered(updatedRecord);
        }

        UpdateRecognitionStatus();
        UpdateRfidRuntimeUi();
        RefreshRecentAlarmHistory();
        _alarmHistoryPage?.Refresh();
        _runLogPage.Refresh();
    }

    private void OnAlarmRecovered(PassageRecord record)
    {
        if (record.AlarmRecoveredAt is not DateTimeOffset recoveredAt)
        {
            return;
        }

        RecordOperationChanges(new[] { AlarmOperationRecordFactory.CreateRecovered(record, recoveredAt) });
        void RefreshAlarmViews()
        {
            RefreshRecentAlarmHistory();
            _alarmHistoryPage?.Refresh();
            _runLogPage.Refresh();
            UpdateRecognitionStatus();
            UpdateRfidRuntimeUi();
        }

        if (Dispatcher.CheckAccess())
        {
            RefreshAlarmViews();
        }
        else
        {
            _ = Dispatcher.BeginInvoke(new Action(RefreshAlarmViews));
        }
    }

    private void RecordOperationChanges(IEnumerable<OperationRecord> records)
    {
        foreach (var record in records)
        {
            OperationRecordWriteSafety.TryAppend(_operationRecordStore, record);
        }
    }

    private void RefreshHistoricalStatistics()
    {
        if (_monitorPage is null)
        {
            return;
        }

        try
        {
            _monitorPage.SetHistoricalStatistics(YardPassageFilter.BuildStatistics(
                _passageRecordStore.Records,
                DateTimeOffset.Now,
                GetCurrentYardStationIds()));
            RefreshRecentAlarmHistory();
            _statisticsPage?.Refresh();
            _lastStatisticsRefresh = DateTimeOffset.Now;
        }
        catch (Exception exception)
        {
            ((App)Application.Current).Logger.Error("读取历史统计失败。", exception);
        }
    }

    private void RefreshRecentAlarmHistory()
    {
        if (_monitorPage is null)
        {
            return;
        }

        try
        {
            var result = _passageRecordStore.Query(new PassageQuery
            {
                IncludeWarnings = true,
                StationIds = GetCurrentYardStationIds(),
                PageIndex = 0,
                PageSize = 6
            });
            _monitorPage.SetRecentAlarmRecords(FilterCurrentYardPassages(result.Items));
            _lastRecentAlarmRefresh = DateTimeOffset.Now;
        }
        catch (Exception exception)
        {
            ((App)Application.Current).Logger.Error("读取最近报警记录失败。", exception);
        }
    }

    private void OnCurrentYardContextChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(CurrentYardContext.CurrentYardId) or nameof(CurrentYardContext.IsGlobalOverview)))
        {
            return;
        }

        ApplyCurrentYardDisplayScope();
        UpdateRecognitionStatus();
        UpdateRfidRuntimeUi();
    }

    private void ApplyCurrentYardDisplayScope()
    {
        if (_monitorPage is null || _yardRfidStationResolver is null)
        {
            return;
        }

        var scope = GetCurrentYardScope();
        if (scope is null)
        {
            return;
        }

        _monitorPage.SetRfidDisplayScope(scope.RfidStations);
        _monitorPage.SetRfidRuntimeStates(FilterCurrentYardStates(GetAllRuntimeStates()));
        _monitorPage.SetRfidPollingInfo(
            _loadedProject?.RfidSettings.PollIntervalMs ?? 200,
            FilterCurrentYardPollingStatuses(GetAllPollingStatuses()));
        var stationIds = scope.IsGlobal ? null : scope.RfidStationIds;
        var selectedYardId = scope.IsGlobal ? null : scope.YardId;
        _settingsPage?.SetSelectedYard(selectedYardId);
        _communicationPage.SetDisplayScope(stationIds, selectedYardId);
        _runLogPage.SetDisplayScope(stationIds, selectedYardId);
        _historyPage?.SetDisplayScope(stationIds, selectedYardId);
        _alarmHistoryPage?.SetDisplayScope(stationIds, selectedYardId);
        _statisticsPage?.SetDisplayScope(stationIds, selectedYardId);
        RefreshRecentAlarmHistory();
    }

    private IReadOnlyList<string>? GetCurrentYardStationIds()
    {
        var scope = GetCurrentYardScope();
        return scope is null || scope.IsGlobal ? null : scope.RfidStationIds;
    }

    private YardRfidStationScope? GetCurrentYardScope()
    {
        if (_yardRfidStationResolver is null)
        {
            return null;
        }

        return _acceptanceOptions.Enabled || _currentYardContext.IsGlobalOverview
            ? _yardRfidStationResolver.ResolveAll()
            : _yardRfidStationResolver.Resolve(_currentYardContext.CurrentYardId!);
    }

    private IEnumerable<StationRuntimeState> FilterCurrentYardStates(IEnumerable<StationRuntimeState> states)
    {
        var scope = GetCurrentYardScope();
        if (scope is null || scope.IsGlobal)
        {
            return states;
        }

        var visibleRfidStationIds = new HashSet<string>(scope.RfidStationIds, StringComparer.OrdinalIgnoreCase);
        return states.Where(state => visibleRfidStationIds.Contains(state.StationId));
    }

    private IEnumerable<RfidStationPollingStatus> FilterCurrentYardPollingStatuses(IEnumerable<RfidStationPollingStatus> statuses)
    {
        var scope = GetCurrentYardScope();
        if (scope is null || scope.IsGlobal)
        {
            return statuses;
        }

        var visibleRfidStationIds = new HashSet<string>(scope.RfidStationIds, StringComparer.OrdinalIgnoreCase);
        return statuses.Where(status => visibleRfidStationIds.Contains(status.StationId));
    }

    private IEnumerable<PassageRecord> FilterCurrentYardPassages(IEnumerable<PassageRecord> records)
    {
        var scope = GetCurrentYardScope();
        if (scope is null || scope.IsGlobal)
        {
            return records;
        }

        var visibleRfidStationIds = new HashSet<string>(scope.RfidStationIds, StringComparer.OrdinalIgnoreCase);
        return records.Where(record => visibleRfidStationIds.Contains(record.StationId));
    }

    private RfidSettings GetRuntimeSettings(ProjectConfig project) => _acceptanceOptions.Enabled
        ? new RfidSettings
        {
            PollIntervalMs = 200,
            ExpectedVehicleCount = project.RfidSettings.ExpectedVehicleCount,
            InterVehicleTimeoutSeconds = _acceptanceOptions.InterVehicleTimeoutSeconds,
            EmptyRfidValue = 0
        }
        : project.RfidSettings;

    private IReadOnlyList<StationRuntimeState> GetAllRuntimeStates() =>
        _yardCommunicationManager?.GetRuntimeStates() ?? Array.Empty<StationRuntimeState>();

    private IReadOnlyList<ExternalAlarmStationDiagnostic> GetExternalAlarmDiagnostics()
    {
        if (_yardCommunicationManager is null)
        {
            return Array.Empty<ExternalAlarmStationDiagnostic>();
        }

        var diagnostics = new List<ExternalAlarmStationDiagnostic>();
        foreach (var context in _yardCommunicationManager.Contexts.Values)
        {
            foreach (var runtimeState in context.RuntimeStates)
            {
                var hasExternalSnapshot = _yardCommunicationManager.TryGetExternalAlarmSnapshot(
                    context.YardId,
                    runtimeState.StationId,
                    out var externalSnapshot,
                    out var externalUpdatedAt);
                diagnostics.Add(new ExternalAlarmStationDiagnostic
                {
                    YardId = context.YardId,
                    StationId = runtimeState.StationId,
                    StationAddress = runtimeState.StationAddress,
                    RuntimeState = runtimeState.LifecycleState,
                    ExternalStatus = hasExternalSnapshot ? externalSnapshot.Status : null,
                    VehicleCount = hasExternalSnapshot
                        ? externalSnapshot.AccumulatedVehicleCount
                        : runtimeState.DetectedVehicleCount,
                    LastUpdatedAt = hasExternalSnapshot ? externalUpdatedAt : runtimeState.LastResponseAt
                });
            }
        }

        return diagnostics;
    }

    private IReadOnlyList<RfidStationPollingStatus> GetAllPollingStatuses() =>
        _yardCommunicationManager?.GetPollingStatuses() ?? Array.Empty<RfidStationPollingStatus>();

    private IReadOnlyDictionary<string, IExternalDataInterface> CreateAlarmForwardSenders(ProjectConfig project)
    {
        if (_acceptanceOptions.Enabled)
        {
            return new Dictionary<string, IExternalDataInterface>(StringComparer.OrdinalIgnoreCase);
        }
        if (project.UsesLegacySharedListener)
        {
            return new Dictionary<string, IExternalDataInterface>(StringComparer.OrdinalIgnoreCase);
        }

        var senders = new Dictionary<string, IExternalDataInterface>(StringComparer.OrdinalIgnoreCase);
        foreach (var configuration in project.YardAlarmForwards.Where(item => item is not null && item.Enabled))
        {
            if (!configuration.TryResolveEndpoint(out var endpoint))
            {
                ((App)Application.Current).Logger.Warning(
                    $"站场 {configuration.YardId} 的报警转发目标无效，已禁用本次运行。 ");
                continue;
            }

            senders[configuration.YardId.Trim()] = new UdpExternalDataInterface(endpoint);
        }

        return senders;
    }

    private IReadOnlyDictionary<string, ExternalAlarmStatusCoordinator> CreateExternalAlarmStatusCoordinators(
        ProjectConfig project)
    {
        if (_acceptanceOptions.Enabled)
        {
            return new Dictionary<string, ExternalAlarmStatusCoordinator>(StringComparer.OrdinalIgnoreCase);
        }

        if (project.UsesLegacySharedListener)
        {
            return new Dictionary<string, ExternalAlarmStatusCoordinator>(StringComparer.OrdinalIgnoreCase);
        }

        var coordinators = new Dictionary<string, ExternalAlarmStatusCoordinator>(StringComparer.OrdinalIgnoreCase);
        foreach (var configuration in project.YardAlarmForwards.Where(item => item is not null && item.Enabled))
        {
            if (!configuration.TryResolveEndpoint(out var endpoint))
            {
                continue;
            }

            var yardCommunication = project.YardCommunications.FirstOrDefault(item =>
                string.Equals(item.YardId, configuration.YardId, StringComparison.OrdinalIgnoreCase));
            if (yardCommunication is null ||
                !yardCommunication.TryResolveExternalAlarmEndpoint(out var localEndpoint))
            {
                ((App)Application.Current).Logger.Warning(
                    $"站场 {configuration.YardId} 的外部报警本机收发端点配置无效，已禁用本次运行。 ");
                continue;
            }

            var sender = new ExternalAlarmUdpChannel(localEndpoint, endpoint);
            coordinators[configuration.YardId.Trim()] = new ExternalAlarmStatusCoordinator(
                new ExternalAlarmFrameCodec(),
                sender);
        }

        return coordinators;
    }

    private bool IsCommunicationHealthy()
    {
        if (_yardCommunicationManager is null || _yardCommunicationManager.Contexts.Count == 0)
        {
            return false;
        }

        var enabledContexts = GetCurrentYardCommunicationContexts()
            .Where(context => context.Configuration.Enabled)
            .ToArray();
        return enabledContexts.Length > 0 &&
            enabledContexts.All(context => context.IsRunning && string.IsNullOrWhiteSpace(context.LastError));
    }

    private IEnumerable<YardCommunicationContext> GetCurrentYardCommunicationContexts()
    {
        if (_yardCommunicationManager is null ||
            _acceptanceOptions.Enabled ||
            _currentYardContext.IsGlobalOverview)
        {
            return _yardCommunicationManager?.Contexts.Values ?? Array.Empty<YardCommunicationContext>();
        }

        var current = _yardCommunicationManager.GetContext(_currentYardContext.CurrentYardId!);
        return current is null ? Array.Empty<YardCommunicationContext>() : new[] { current };
    }

    private IReadOnlyList<YardCommunicationConfig> CreateAcceptanceYardCommunications() => new[]
    {
        new YardCommunicationConfig
        {
            YardId = "560",
            ListenIp = _acceptanceOptions.ListenAddress.ToString(),
            ListenPort = _acceptanceOptions.ListenPort,
            Enabled = true
        },
        new YardCommunicationConfig
        {
            YardId = "620",
            ListenIp = _acceptanceOptions.ListenAddress.ToString(),
            ListenPort = _acceptanceOptions.ListenPort620,
            Enabled = true
        }
    };

    private IReadOnlyList<RfidStationConfig> CreateAcceptanceStations() => new[]
    {
        CreateAcceptanceStation(0x01, "560", _acceptanceOptions.SimulatorPort),
        CreateAcceptanceStation(0x04, "620", _acceptanceOptions.SimulatorPort620)
    };

    private static IReadOnlyList<VehicleConfig> CreateAcceptanceVehicles() => new[]
    {
        CreateAcceptanceVehicle("K001", "11节验收车辆001", 0x1001, 0x1101),
        CreateAcceptanceVehicle("K002", "11节验收车辆002", 0x1002, 0x1201)
    }
        .Concat(AcceptanceVehicleCatalog.CreateVehicles())
        .ToArray();

    private static VehicleConfig CreateAcceptanceVehicle(
        string vehicleNo,
        string vehicleName,
        ushort headRfid,
        ushort firstWagonRfid,
        int wagonCount = 10)
    {
        var tags = new List<VehicleTagConfig>
        {
            new() { Position = 0, Rfid = headRfid, Type = VehicleTagType.Head }
        };
        tags.AddRange(Enumerable.Range(0, wagonCount).Select(index => new VehicleTagConfig
        {
            Position = index + 1,
            Rfid = (ushort)(firstWagonRfid + index),
            Type = VehicleTagType.Carriage
        }));

        return new VehicleConfig
        {
            VehicleNo = vehicleNo,
            VehicleName = vehicleName,
            HeadRfid = headRfid,
            Enabled = true,
            Tags = tags
        };
    }

    private RfidStationConfig CreateAcceptanceStation(byte address, string yardId, int simulatorPort) => new()
    {
        StationId = $"ACCEPTANCE-RFID-{address:X2}",
        Name = $"验收基站 {address:X2}",
        YardId = yardId,
        ProtocolAddress = address,
        Enabled = true,
        Mode = 0x04,
        CommandBytes = new byte[4],
        RequestPayload = new byte[28],
        IpAddress = _acceptanceOptions.SimulatorAddress.ToString(),
        Port = simulatorPort,
        DestinationEndpoint = new IPEndPoint(_acceptanceOptions.SimulatorAddress, simulatorPort)
    };

    private void UpdateHeaderStatusIndicators()
    {
        var stationStates = FilterCurrentYardStates(GetAllRuntimeStates()).ToArray();
        var stationStatus = GetRfidStatus(stationStates);
        var systemStatus = !_rfidListenerHealthy
            ? (StatusIndicatorState.Error, "系统异常")
            : stationStatus.State is StatusIndicatorState.Warning or StatusIndicatorState.Unavailable
                ? (StatusIndicatorState.Warning, "系统告警")
                : (StatusIndicatorState.Healthy, "系统正常");

        ApplyStatusIndicator(HeaderSystemStatusDot, HeaderSystemStatusText, systemStatus.Item2, systemStatus.Item1);
        ApplyStatusIndicator(
            HeaderRfidStatusDot,
            RfidStationStatusText,
            !_rfidListenerHealthy ? "RFID监听异常" : stationStatus.Text,
            !_rfidListenerHealthy ? StatusIndicatorState.Error : stationStatus.State);
        ApplyStatusIndicator(
            HeaderExternalStatusDot,
            HeaderExternalStatusText,
            _externalInterfaceAvailable ? "报警 UDP 已配置" : "报警 UDP 未配置",
            _externalInterfaceAvailable ? StatusIndicatorState.Healthy : StatusIndicatorState.Unavailable);
    }

    private static (StatusIndicatorState State, string Text) GetRfidStatus(IReadOnlyList<StationRuntimeState> states)
    {
        if (states.Count == 0)
        {
            return (StatusIndicatorState.Unavailable, "RFID基站未配置");
        }

        var onlineCount = states.Count(state => state.CommunicationState == StationCommunicationState.Online);
        if (onlineCount == states.Count)
        {
            return (StatusIndicatorState.Healthy, $"RFID基站 在线 {onlineCount}/{states.Count}");
        }

        return (StatusIndicatorState.Warning, $"RFID基站 在线 {onlineCount}/{states.Count}");
    }

    private static void ApplyStatusIndicator(
        System.Windows.Shapes.Ellipse dot,
        TextBlock text,
        string label,
        StatusIndicatorState state)
    {
        var brush = GetStatusBrush(state);
        dot.Fill = brush;
        dot.Stroke = brush;
        dot.Effect = new DropShadowEffect
        {
            Color = brush is SolidColorBrush solid ? solid.Color : Colors.Gray,
            BlurRadius = 6,
            ShadowDepth = 0,
            Opacity = 0.5
        };
        text.Text = label;
        text.Foreground = state is StatusIndicatorState.Warning or StatusIndicatorState.Error
            ? brush
            : Application.Current?.TryFindResource("TextSecondaryBrush") as Brush ?? brush;
    }

    private static Brush GetStatusBrush(StatusIndicatorState state)
    {
        var resourceKey = state switch
        {
            StatusIndicatorState.Healthy => "SuccessBrush",
            StatusIndicatorState.Warning => "WarningBrush",
            StatusIndicatorState.Error => "AlarmBrush",
            _ => "OfflineBrush"
        };
        return Application.Current?.TryFindResource(resourceKey) as Brush ?? Brushes.Gray;
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        CurrentDateText.Text = now.ToString("yyyy-MM-dd");
        CurrentWeekdayText.Text = now.ToString("dddd", System.Globalization.CultureInfo.GetCultureInfo("zh-CN"));
        CurrentTimeText.Text = now.ToString("HH:mm:ss");
    }

    private void SelectNavigationButton(Button button)
    {
        ClearButtonSelection(_activeNavigationButton);
        button.SetResourceReference(Button.BackgroundProperty, "AccentButtonBrush");
        button.SetResourceReference(Button.BorderBrushProperty, "PrimaryBlueBrush");
        button.SetResourceReference(Button.ForegroundProperty, "TextPrimaryBrush");
        _activeNavigationButton = button;
    }

    private void SelectStationButton(Button? button)
    {
        ClearButtonSelection(_activeStationButton);
        if (button is null)
        {
            return;
        }

        button.SetResourceReference(Button.BackgroundProperty, "AccentButtonBrush");
        button.SetResourceReference(Button.BorderBrushProperty, "PrimaryBlueBrush");
        button.SetResourceReference(Button.ForegroundProperty, "TextPrimaryBrush");
        _activeStationButton = button;
    }

    private static void ClearButtonSelection(Button? button)
    {
        if (button is null)
        {
            return;
        }

        button.ClearValue(Button.BackgroundProperty);
        button.ClearValue(Button.BorderBrushProperty);
        button.ClearValue(Button.ForegroundProperty);
    }

    private static string FormatStationName(StationConfig station) =>
        station.Name.Replace(" 站场", "水平");
}
