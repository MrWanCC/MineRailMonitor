namespace MineRailMonitor.Core.Tests;

public sealed class OperationRecordCoverageTests
{
    [Fact]
    public void Final_settings_persistence_callbacks_check_admin_mode()
    {
        var mainWindow = ReadSource("src", "MineRailMonitor", "MainWindow.xaml.cs");
        var rfidSettingsSave = ExtractMethod(mainWindow, "private async Task<bool> SaveRfidSettingsAsync", "private static bool AreRfidSettingsEqual");
        var stationSave = ExtractMethod(mainWindow, "private async Task<bool> SaveRfidStationsAsync", "private bool IsRfidStationBound");

        Assert.Contains("if (_loadedProject is null || _settingsPage is null || !_adminModeService.IsAdmin)", rfidSettingsSave, StringComparison.Ordinal);
        Assert.Contains("if (_loadedProject is null || _settingsPage is null || !_adminModeService.IsAdmin)", stationSave, StringComparison.Ordinal);
    }

    [Fact]
    public void Communication_test_and_database_backup_final_entries_require_admin_mode()
    {
        var mainWindow = ReadSource("src", "MineRailMonitor", "MainWindow.xaml.cs");
        var communicationSend = ExtractMethod(mainWindow, "private async Task SendCommunicationTestAsync", "private async Task<string?> CreateManualBackupAsync");
        var backup = ExtractMethod(mainWindow, "private async Task<string?> CreateManualBackupAsync", "private void AcknowledgeAlarm");

        Assert.Contains("if (!_adminModeService.IsAdmin)", communicationSend, StringComparison.Ordinal);
        Assert.Contains("if (!_adminModeService.IsAdmin)", backup, StringComparison.Ordinal);
    }

    [Fact]
    public void Programmatic_selection_of_admin_settings_tabs_is_rejected()
    {
        var settingsPage = ReadSource("src", "MineRailMonitor", "Pages", "SettingsPage.xaml.cs");
        var selectionHandler = ExtractMethod(settingsPage, "private void OnSettingsTabSelectionChanged", "private void OnViewRangeChanged");

        Assert.Contains("_adminModeService.IsAdmin", selectionHandler, StringComparison.Ordinal);
        Assert.Contains("SettingsTabs.SelectedIndex = 0", selectionHandler, StringComparison.Ordinal);
    }

    [Fact]
    public void Operation_records_are_not_queried_or_retained_in_normal_mode()
    {
        var settingsPage = ReadSource("src", "MineRailMonitor", "Pages", "SettingsPage.xaml.cs");
        var refresh = ExtractMethod(settingsPage, "public void RefreshOperationLogs()", "public void SetSelectedYard");

        Assert.Contains("_adminModeService.IsAdmin", refresh, StringComparison.Ordinal);
        Assert.Contains("OperationLogGrid.ItemsSource = Array.Empty<OperationRecordDisplayItem>()", refresh, StringComparison.Ordinal);
        Assert.True(refresh.IndexOf("_adminModeService.IsAdmin", StringComparison.Ordinal) <
                    refresh.IndexOf("QueryPage", StringComparison.Ordinal));

        var runLogPage = ReadSource("src", "MineRailMonitor", "Pages", "RunLogPage.xaml.cs");
        var runLogRefresh = ExtractMethod(runLogPage, "public void Refresh()", "private void OnAdminModePropertyChanged");
        Assert.Contains("_adminModeService?.IsAdmin == true", runLogRefresh, StringComparison.Ordinal);
        Assert.Contains("_operationRecordStore.QueryPage", runLogRefresh, StringComparison.Ordinal);
        Assert.True(runLogRefresh.IndexOf("_adminModeService?.IsAdmin == true", StringComparison.Ordinal) <
                    runLogRefresh.IndexOf("_operationRecordStore.QueryPage", StringComparison.Ordinal));
    }

    [Fact]
    public void Admin_transition_backup_and_communication_configuration_operations_are_recorded()
    {
        var mainWindow = ReadSource("src", "MineRailMonitor", "MainWindow.xaml.cs");
        var adminModeHandler = ExtractMethod(mainWindow, "private void OnAdminModeStateChanged", "private void UpdateAdminModeBanner");
        var formatter = ReadSource("src", "MineRailMonitor.Core", "Services", "ConfigurationOperationRecordFormatter.cs");

        Assert.Contains("ADMIN_LOGIN", adminModeHandler, StringComparison.Ordinal);
        Assert.Contains("ADMIN_LOGOUT", adminModeHandler, StringComparison.Ordinal);
        Assert.Contains("DATABASE_BACKUP", ExtractMethod(mainWindow, "private async Task<string?> CreateManualBackupAsync", "private void AcknowledgeAlarm"), StringComparison.Ordinal);
        Assert.Contains("DATABASE_BACKUP_FAILED", ExtractMethod(mainWindow, "private async Task<string?> CreateManualBackupAsync", "private void AcknowledgeAlarm"), StringComparison.Ordinal);
        Assert.Contains("COMMUNICATION_CONFIG_CHANGED", formatter, StringComparison.Ordinal);
        Assert.Contains("GetYardCommunicationChanges", mainWindow, StringComparison.Ordinal);
        Assert.Contains("GetYardAlarmForwardChanges", mainWindow, StringComparison.Ordinal);
    }

    [Fact]
    public void Backup_button_rejects_non_admin_before_invoking_backup_callback()
    {
        var settingsPage = ReadSource("src", "MineRailMonitor", "Pages", "SettingsPage.xaml.cs");
        var backupClick = ExtractMethod(settingsPage, "private async void OnCreateBackupClick", "private void OnSettingsTabSelectionChanged");

        Assert.Contains("if (!_adminModeService.IsAdmin)", backupClick, StringComparison.Ordinal);
        Assert.True(backupClick.IndexOf("_adminModeService.IsAdmin", StringComparison.Ordinal) <
                    backupClick.IndexOf("BackupRequested", StringComparison.Ordinal));
    }

    private static string ExtractMethod(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Method marker not found: {startMarker}");
        Assert.True(end > start, $"End marker not found: {endMarker}");
        return source.Substring(start, end - start);
    }

    private static string ReadSource(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = segments.Aggregate(directory.FullName, Path.Combine);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate source file: {Path.Combine(segments)}");
    }
}
