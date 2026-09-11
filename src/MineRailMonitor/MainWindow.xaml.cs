using System.IO;
using System.Net;
using System.Configuration;
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
using MineRailMonitor.Core.Protocol;
using MineRailMonitor.Core.Recognition;
using MineRailMonitor.Core.Services;
using MineRailMonitor.Infrastructure.Configuration;
using MineRailMonitor.Infrastructure.Persistence;
using MineRailMonitor.Pages;

namespace MineRailMonitor;

public partial class MainWindow : Window
{
    private readonly IProjectConfigService _configService;
    private readonly AdminModeService _adminModeService;
    private readonly string _projectDirectory;
    private readonly AcceptanceCommandLineOptions _acceptanceOptions;
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private MonitorPage? _monitorPage;
    private readonly CommunicationPage _communicationPage;
    private RfidFrameParser _rfidFrameParser;
    private SettingsPage? _settingsPage;
    private HistoryPage? _historyPage;
    private AlarmHistoryPage? _alarmHistoryPage;
    private RfidStatisticsPage? _statisticsPage;
    private RfidRuntimeCoordinator? _rfidRuntimeCoordinator;
    private readonly SqlitePassageRecordStore _passageRecordStore;
    private AcceptanceRuntimeStateWriter? _acceptanceRuntimeStateWriter;
    private DateTimeOffset _lastStatisticsRefresh = DateTimeOffset.MinValue;
    private RfidStationPoller? _rfidPoller;
    private CancellationTokenSource? _rfidPollerCts;
    private RfidUdpTransport? _rfidUdpTransport;
    private ProjectConfig? _loadedProject;
    private Button? _activeNavigationButton;
    private Button? _activeStationButton;
    private bool _allowWindowClose;
    private bool _rfidListenerHealthy;
    private bool _externalInterfaceAvailable;

    private enum StatusIndicatorState
    {
        Healthy,
        Warning,
        Error,
        Unavailable
    }

    private const int WM_GETMINMAXINFO = 0x0024;
    private const uint MonitorDefaultToNearest = 0x00000002;

    public MainWindow()
    {
        InitializeComponent();
        _acceptanceOptions = ((App)Application.Current).AcceptanceOptions;
        _externalInterfaceAvailable = false;
        _projectDirectory = _acceptanceOptions.Enabled
            ? Path.Combine(AppContext.BaseDirectory, "Projects", "Example")
            : ResolveProjectDirectory();
        if (_acceptanceOptions.Enabled)
        {
            WindowState = WindowState.Minimized;
            ShowInTaskbar = false;
        }
        _adminModeService = ((App)Application.Current).AdminModeService;
        _adminModeService.PropertyChanged += OnAdminModeStateChanged;
        UpdateAdminModeBanner();
        _configService = new ProjectConfigService(((App)Application.Current).Logger);
        _passageRecordStore = new SqlitePassageRecordStore(
            _acceptanceOptions.Enabled
                ? _acceptanceOptions.DatabasePath!
                : Path.Combine(AppContext.BaseDirectory, "Data", "MineRailMonitor.db"));
        _communicationPage = new CommunicationPage();
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

    private static string ResolveProjectDirectory()
    {
        var projectsDirectory = Path.Combine(AppContext.BaseDirectory, "Projects");
        var defaultDirectory = Path.Combine(projectsDirectory, "Default");
        return File.Exists(Path.Combine(defaultDirectory, "project.json"))
            ? defaultDirectory
            : Path.Combine(projectsDirectory, "Example");
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
        StartRfidListener();
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
            var error = result.Errors.Count == 0
                ? "项目配置未返回有效内容。"
                : string.Join(Environment.NewLine, result.Errors);
            PageContent.Content = new PlaceholderPage("项目配置加载失败", error);
            return;
        }

        _loadedProject = result.Project;
        var runtimeSettings = GetRuntimeSettings(result.Project);
        _rfidFrameParser = new RfidFrameParser(new RfidFrameParserOptions
        {
            EmptyRfidValue = runtimeSettings.EmptyRfidValue
        });
        var settingsStations = _acceptanceOptions.Enabled ? CreateAcceptanceStations() : result.Project.RfidStations;
        _settingsPage = new SettingsPage(
            runtimeSettings,
            _adminModeService,
            settingsStations,
            stationsEditable: !_acceptanceOptions.Enabled);
        _settingsPage.SaveRequested += SaveRfidSettingsAsync;
        _settingsPage.StationsSaveRequested += SaveRfidStationsAsync;
        _rfidRuntimeCoordinator = CreateRfidRuntimeCoordinator(result.Project, runtimeSettings);
        if (_rfidRuntimeCoordinator is not null)
        {
            _rfidRuntimeCoordinator.RestorePendingClear(_passageRecordStore.GetPendingClear());
            if (_acceptanceOptions.Enabled)
            {
                _acceptanceRuntimeStateWriter = new AcceptanceRuntimeStateWriter(
                    _acceptanceOptions.RuntimeStatePath!,
                    _rfidRuntimeCoordinator);
                _rfidRuntimeCoordinator.CommandSent += OnRfidCommandSent;
                _acceptanceRuntimeStateWriter.Write("loaded");
            }
        }
        _historyPage = new HistoryPage(
            _passageRecordStore,
            _acceptanceOptions.Enabled ? settingsStations : result.Project.RfidStations);
        _alarmHistoryPage = new AlarmHistoryPage(
            _passageRecordStore,
            _acceptanceOptions.Enabled ? settingsStations : result.Project.RfidStations);
        _statisticsPage = new RfidStatisticsPage(
            _passageRecordStore,
            _acceptanceOptions.Enabled ? settingsStations : result.Project.RfidStations);
        _communicationPage.ConfigureStations(settingsStations, SendCommunicationTestAsync);
        StartRfidPoller(result.Project, runtimeSettings);
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

        _monitorPage = new MonitorPage(_configService, _projectDirectory, _adminModeService);
        _monitorPage.SetRfidStations(settingsStations);
        _monitorPage.SetSystemHealth(databaseHealthy: true, externalInterfaceAvailable: _externalInterfaceAvailable);
        _monitorPage.SetSystemRfidStatus(_rfidListenerHealthy);
        _monitorPage.ResetRequested += (_, _) => _monitorPage.Viewport.ResetView();
        _monitorPage.AddSystemEvent("配置", "配置加载成功");
        PageContent.Content = _monitorPage;
        ShowStation(result.Project.DefaultStationId);
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

        if (page == "Communication")
        {
            PageContent.Content = _communicationPage;
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
            PageContent.Content = _settingsPage;
            return;
        }

        if (page == "History" && _historyPage is not null)
        {
            _historyPage.Refresh();
            PageContent.Content = _historyPage;
            return;
        }

        if (page == "Alarms" && _alarmHistoryPage is not null)
        {
            _alarmHistoryPage.Refresh();
            PageContent.Content = _alarmHistoryPage;
            return;
        }

        var title = page switch
        {
            "Rfid" => "RFID统计",
            "Query" => "数据查询",
            "Alarms" => "报警记录",
            "History" => "历史查询",
            "Communication" => "通信调试",
            "Settings" => "系统设置",
            "Overview" => "全局总览",
            _ => "页面"
        };
        PageContent.Content = new PlaceholderPage(title, "本阶段仅提供页面骨架，业务功能将在后续阶段实现。");
    }

    private async Task<bool> SaveRfidSettingsAsync(RfidSettings settings)
    {
        if (_loadedProject is null || _settingsPage is null)
        {
            return false;
        }
        var result = await _configService.SaveRfidSettingsAsync(_projectDirectory, settings);
        if (!result.Succeeded)
        {
            _settingsPage.SetSaveResult(string.Join(Environment.NewLine, result.Errors), true);
            return false;
        }
        _loadedProject.RfidSettings = settings;
        _rfidFrameParser = new RfidFrameParser(new RfidFrameParserOptions { EmptyRfidValue = settings.EmptyRfidValue });
        _rfidRuntimeCoordinator?.UpdateDefaults(settings);
        StopRfidPoller();
        StartRfidPoller(_loadedProject);
        _settingsPage.SetSaveResult("设置已保存。", false);
        return true;
    }

    private async Task<bool> SaveRfidStationsAsync(IReadOnlyList<RfidStationConfig> stations)
    {
        if (_loadedProject is null || _settingsPage is null)
        {
            return false;
        }
        if (_acceptanceOptions.Enabled)
        {
            _settingsPage.SetSaveResult("验收模式禁止保存正式基站配置。", true);
            return false;
        }

        var result = await _configService.SaveRfidStationsAsync(_projectDirectory, stations);
        if (!result.Succeeded)
        {
            _settingsPage.SetSaveResult(string.Join(Environment.NewLine, result.Errors), true);
            return false;
        }

        _loadedProject.RfidStations = stations.ToArray();
        _monitorPage?.SetRfidStations(_loadedProject.RfidStations);
        var pendingClearRecords = _passageRecordStore.GetPendingClear();
        if (_rfidRuntimeCoordinator is not null)
        {
            _rfidRuntimeCoordinator.CommandSent -= OnRfidCommandSent;
        }
        _rfidRuntimeCoordinator = CreateRfidRuntimeCoordinator(_loadedProject, _loadedProject.RfidSettings);
        _rfidRuntimeCoordinator?.RestorePendingClear(pendingClearRecords);
        StopRfidPoller();
        StartRfidPoller(_loadedProject);
        UpdateRfidRuntimeUi();
        _settingsPage.SetSaveResult("设置与RFID基站配置已保存。", false);
        return true;
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

    private void StartRfidListener()
    {
        _rfidListenerHealthy = false;
        UpdateHeaderStatusIndicators();
        try
        {
            var address = _acceptanceOptions.Enabled
                ? _acceptanceOptions.ListenAddress
                : IPAddress.Parse(ConfigurationManager.AppSettings["RfidUdpListenAddress"]!);
            var port = _acceptanceOptions.Enabled
                ? _acceptanceOptions.ListenPort
                : int.Parse(ConfigurationManager.AppSettings["RfidUdpListenPort"]!, System.Globalization.CultureInfo.InvariantCulture);
            if (address is null || port <= 0)
            {
                throw new InvalidOperationException("RFID UDP 监听配置无效。");
            }

            _rfidUdpTransport = new RfidUdpTransport(address, port);
            _rfidUdpTransport.DatagramReceived += OnRfidDatagramReceived;
            _rfidUdpTransport.ReceiveError += OnRfidReceiveError;
            var endpoint = _rfidUdpTransport.LocalEndPoint;
            _rfidListenerHealthy = true;
            UpdateHeaderStatusIndicators();
            _communicationPage.SetListenerStatus($"监听中：{endpoint}，等待数据");
            _ = _rfidUdpTransport.StartAsync(CancellationToken.None);
            ((App)Application.Current).Logger.Information($"RFID UDP 监听已启动：{endpoint}");
        }
        catch (Exception exception)
        {
            _rfidListenerHealthy = false;
            UpdateHeaderStatusIndicators();
            _communicationPage.SetError(exception);
            ((App)Application.Current).Logger.Error("RFID UDP 监听启动失败。", exception);
        }
    }

    private void OnRfidDatagramReceived(object? sender, RfidUdpDatagramEventArgs args)
    {
        var hex = BitConverter.ToString(args.Data).Replace('-', ' ');
        var message = $"RFID UDP RX {args.RemoteEndPoint} {args.Data.Length} Bytes {hex}";
        var responseMatched = false;
        if (args.IsValid)
        {
            if (args.Data.Length >= 3)
            {
                responseMatched = _rfidPoller?.RecordResponse(args.RemoteEndPoint, args.Data[2], args.ReceivedAt) == true;
            }
            if (responseMatched)
            {
                ((App)Application.Current).Logger.Information(message);
            }
            else
            {
                ((App)Application.Current).Logger.Warning($"未匹配 RFID 基站端点或协议地址，已丢弃：{message}");
            }
        }
        else
        {
            ((App)Application.Current).Logger.Warning($"非法 RFID UDP 帧：{args.ValidationError}；{message}");
        }

        RfidStationFrame? parsedFrame = null;
        if (responseMatched && args.Data[3] == 0x04)
        {
            _rfidFrameParser.TryParse(args.Data, args.RemoteEndPoint, args.ReceivedAt, out parsedFrame);
        }

        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            var recognitionSession = (StationRecognitionSession?)null;
            if (parsedFrame is not null)
            {
                recognitionSession = _rfidRuntimeCoordinator?.ProcessFrame(parsedFrame);
                if (recognitionSession is not null)
                {
                    _monitorPage?.SetRecognitionSnapshot(parsedFrame, recognitionSession);
                }
                UpdateRecognitionStatus();
                UpdateRfidRuntimeUi();
                _acceptanceRuntimeStateWriter?.Write("frame");
            }
            _communicationPage.AddDatagram(args, parsedFrame, recognitionSession);
            if (responseMatched)
            {
                _rfidListenerHealthy = true;
                UpdateHeaderStatusIndicators();
                _monitorPage?.SetSystemRfidStatus(true);
                _communicationPage.SetListenerStatus($"监听中：{_rfidUdpTransport?.LocalEndPoint}，最近收到有效报文");
            }
        }));
    }

    private void OnRfidReceiveError(Exception exception)
    {
        ((App)Application.Current).Logger.Error("RFID UDP 接收异常。", exception);
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            _rfidListenerHealthy = false;
            UpdateHeaderStatusIndicators();
            _monitorPage?.SetSystemRfidStatus(false);
            _communicationPage.SetError(exception);
        }));
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _adminModeService.PropertyChanged -= OnAdminModeStateChanged;
        if (_rfidRuntimeCoordinator is not null)
        {
            _rfidRuntimeCoordinator.CommandSent -= OnRfidCommandSent;
        }
        _clockTimer.Stop();
        _rfidUdpTransport?.Stop();
        _rfidUdpTransport?.Dispose();
        StopRfidPoller();
        _acceptanceRuntimeStateWriter?.Write("closed");
        _acceptanceRuntimeStateWriter?.Dispose();
        _passageRecordStore.Dispose();
    }

    private void OnRfidCommandSent(byte stationAddress, RfidPollCommand command, DateTimeOffset sentAt)
    {
        if (command == RfidPollCommand.Clear)
        {
            _acceptanceRuntimeStateWriter?.Write("clear-command-sent");
        }
    }

    private void OnAdminModeStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AdminModeService.IsAdmin))
        {
            UpdateAdminModeBanner();
            _monitorPage?.HandleAdminModeChanged(_adminModeService.IsAdmin);
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
        if (_monitorPage is not null && !await _monitorPage.TryLeaveMapEditingAsync("退出管理员模式"))
        {
            return;
        }

        if (_settingsPage is not null && !await _settingsPage.TryLeaveAsync("退出管理员模式"))
        {
            return;
        }

        _adminModeService.ExitAdminMode();
    }

    private async void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowWindowClose || (_monitorPage is null && _settingsPage is null))
        {
            return;
        }

        if (_monitorPage is not null && _monitorPage.HasUnsavedMapChanges)
        {
            e.Cancel = true;
            if (!await _monitorPage.TryLeaveMapEditingAsync("关闭程序"))
            {
                return;
            }
        }

        if (_settingsPage is not null && _settingsPage.HasUnsavedChanges)
        {
            e.Cancel = true;
            if (!await _settingsPage.TryLeaveAsync("关闭程序"))
            {
                return;
            }
        }

        if (e.Cancel)
        {
            _allowWindowClose = true;
            Close();
        }
    }

    private static ushort ReadEmptyRfidValue()
    {
        var value = ConfigurationManager.AppSettings["EmptyRfidValue"];
        return ushort.TryParse(value, out var parsedValue) ? parsedValue : (ushort)0;
    }

    private void StartRfidPoller(ProjectConfig project, RfidSettings? runtimeSettings = null)
    {
        var stations = _acceptanceOptions.Enabled ? CreateAcceptanceStations() : project.RfidStations;
        var settings = runtimeSettings ?? project.RfidSettings;
        if (_rfidUdpTransport is null || stations.Count == 0)
        {
            ((App)Application.Current).Logger.Information("未配置真实 RFID 基站地址，轮询器保持停止。");
            return;
        }

        try
        {
            _rfidPoller = new RfidStationPoller(
                stations,
                settings.PollIntervalMs,
                _rfidUdpTransport,
                new SystemRfidTimeProvider(),
                _rfidRuntimeCoordinator);
            _rfidPollerCts = new CancellationTokenSource();
            _ = _rfidPoller.RunAsync(_rfidPollerCts.Token);
            ((App)Application.Current).Logger.Information($"RFID 轮询器已启动，启用基站：{stations.Count}。");
        }
        catch (Exception exception)
        {
            ((App)Application.Current).Logger.Error("RFID 轮询器启动失败。", exception);
        }
    }

    private void StopRfidPoller()
    {
        _rfidPollerCts?.Cancel();
        _rfidPollerCts?.Dispose();
        _rfidPollerCts = null;
        _rfidPoller = null;
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
        _rfidRuntimeCoordinator?.Evaluate(DateTimeOffset.Now);
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
        // 未检测到车头标签、首个识别标签不是有效车头标签、检测到多个车头标签、协议数据告警。
        if (_monitorPage is null || _rfidRuntimeCoordinator is null)
        {
            return;
        }

        var states = _rfidRuntimeCoordinator.States.Values.ToArray();
        var alarm = states.FirstOrDefault(state =>
            state.CommunicationState != StationCommunicationState.Offline &&
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
        if (_monitorPage is null)
        {
            UpdateHeaderStatusIndicators();
            return;
        }

        _monitorPage.SetRfidRuntimeStates(_rfidRuntimeCoordinator?.States.Values ?? Array.Empty<StationRuntimeState>());
        _monitorPage.SetRfidPollingInfo(
            _loadedProject?.RfidSettings.PollIntervalMs ?? 200,
            _rfidPoller?.EndpointStatuses.Values ?? Array.Empty<RfidStationPollingStatus>());
        _communicationPage.SetStationStatuses(
            _rfidPoller?.EndpointStatuses.Values ?? Array.Empty<RfidStationPollingStatus>());
        _statisticsPage?.SetRuntimeStates(_rfidRuntimeCoordinator?.States.Values ?? Array.Empty<StationRuntimeState>());
        _monitorPage.SetSystemRfidStatus(_rfidListenerHealthy);
        UpdateHeaderStatusIndicators();
    }

    private async Task SendCommunicationTestAsync(RfidStationConfig station, RfidPollCommand command)
    {
        if (_rfidUdpTransport is null)
        {
            throw new InvalidOperationException("RFID UDP 监听通道尚未就绪。");
        }

        if (!station.TryResolveEndpoint(out var endpoint))
        {
            throw new InvalidOperationException($"RFID基站端点配置无效：{station.StationId}。");
        }

        var request = RfidRequestFrameBuilder.Build(station, command);
        await _rfidUdpTransport.SendAsync(request, endpoint, CancellationToken.None);
    }

    private void RefreshHistoricalStatistics()
    {
        if (_monitorPage is null)
        {
            return;
        }

        try
        {
            _monitorPage.SetHistoricalStatistics(_passageRecordStore.GetStatistics(DateTimeOffset.Now));
            _lastStatisticsRefresh = DateTimeOffset.Now;
        }
        catch (Exception exception)
        {
            ((App)Application.Current).Logger.Error("读取历史统计失败。", exception);
        }
    }

    private RfidRuntimeCoordinator? CreateRfidRuntimeCoordinator(ProjectConfig project, RfidSettings runtimeSettings)
    {
        var stations = _acceptanceOptions.Enabled ? CreateAcceptanceStations() : project.RfidStations;
        return stations.Count(station => station.Enabled) == 0
            ? null
            : new RfidRuntimeCoordinator(stations, runtimeSettings, _passageRecordStore);
    }

    private RfidSettings GetRuntimeSettings(ProjectConfig project) => _acceptanceOptions.Enabled
        ? new RfidSettings
        {
            PollIntervalMs = 200,
            ExpectedVehicleCount = 11,
            InterVehicleTimeoutSeconds = _acceptanceOptions.InterVehicleTimeoutSeconds,
            EmptyRfidValue = 0
        }
        : project.RfidSettings;

    private IReadOnlyList<RfidStationConfig> CreateAcceptanceStations() => new[]
    {
        CreateAcceptanceStation(0x01),
        CreateAcceptanceStation(0x04)
    };

    private RfidStationConfig CreateAcceptanceStation(byte address) => new()
    {
        StationId = $"ACCEPTANCE-RFID-{address:X2}",
        Name = $"验收基站 {address:X2}",
        ProtocolAddress = address,
        Enabled = true,
        Mode = 0x04,
        CommandBytes = new byte[4],
        RequestPayload = new byte[28],
        IpAddress = _acceptanceOptions.SimulatorAddress.ToString(),
        Port = _acceptanceOptions.SimulatorPort,
        DestinationEndpoint = new IPEndPoint(_acceptanceOptions.SimulatorAddress, _acceptanceOptions.SimulatorPort)
    };

    private void UpdateHeaderStatusIndicators()
    {
        var stationStates = _rfidRuntimeCoordinator?.States.Values.ToArray() ?? Array.Empty<StationRuntimeState>();
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
            _externalInterfaceAvailable ? "外部接口正常" : "外部接口未接入",
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
