namespace MineRailMonitor.Core.Tests;

public sealed class DatabaseRecoveryDialogMarkupTests
{
    [Fact]
    public void Recovery_dialog_is_a_startup_window_not_a_main_window_owned_dialog()
    {
        var markup = ReadMarkup();

        Assert.Contains("WindowStartupLocation=\"CenterScreen\"", markup);
        Assert.Contains("ShowInTaskbar=\"True\"", markup);
        Assert.DoesNotContain("WindowStartupLocation=\"CenterOwner\"", markup);
        Assert.DoesNotContain("Owner=", markup);
    }

    [Fact]
    public void Recovery_dialog_uses_existing_industrial_visual_resources()
    {
        var markup = ReadMarkup();

        Assert.Contains("Colors.xaml", markup);
        Assert.Contains("Typography.xaml", markup);
        Assert.Contains("Cards.xaml", markup);
        Assert.Contains("WindowStyle=\"None\"", markup);
        Assert.Contains("ResizeMode=\"NoResize\"", markup);
        Assert.Contains("Width=\"760\"", markup);
        Assert.Contains("x:Name=\"DialogBorder\"", markup);
    }

    [Fact]
    public void Recovery_dialog_exposes_the_fixed_action_contract()
    {
        var code = ReadCode();

        Assert.Contains("public enum DatabaseRecoveryDialogAction", code);
        Assert.Contains("Recover,", code);
        Assert.Contains("ResumeRecovery,", code);
        Assert.Contains("Retry,", code);
        Assert.Contains("OpenDataDirectory,", code);
        Assert.Contains("Exit", code);
        Assert.Contains("public sealed class DatabaseRecoveryDialogResult", code);
        Assert.Contains("public DatabaseRecoveryDialogAction Action { get; }", code);
        Assert.Contains("public SqliteBackupCandidate? Candidate { get; }", code);
        Assert.Contains("public DatabaseRecoveryDialog(DatabaseStartupDecision decision)", code);
        Assert.Contains("public DatabaseRecoveryDialogResult Result => _result", code);
    }

    [Fact]
    public void Closing_without_action_defaults_to_exit()
    {
        var code = ReadCode();
        var markup = ReadMarkup();

        Assert.Contains("_result = new DatabaseRecoveryDialogResult(DatabaseRecoveryDialogAction.Exit, null)", code);
        Assert.Contains("Closing=\"OnDialogClosing\"", markup);
        Assert.Contains("new DatabaseRecoveryDialogResult(DatabaseRecoveryDialogAction.Exit, null)", code);
    }

    [Fact]
    public void Corrupt_state_shows_health_summary_and_candidates()
    {
        var markup = ReadMarkup();
        var code = ReadCode();

        Assert.Contains("数据库完整性异常", code);
        Assert.Contains("生产数据库路径", markup);
        Assert.Contains("quick_check", markup);
        Assert.Contains("integrity_check", markup);
        Assert.Contains("foreign_key_check", markup);
        Assert.Contains("x:Name=\"CandidateList\"", markup);
        Assert.Contains("x:Name=\"CandidatePathText\"", markup);
        Assert.Contains("x:Name=\"CandidateTimestampText\"", markup);
        Assert.Contains("_decision.Candidates", code);
        Assert.Contains("备份时间之后产生的历史记录可能丢失", markup);
    }

    [Fact]
    public void Corrupt_state_selects_only_gate_supplied_candidates()
    {
        var code = ReadCode();

        Assert.Contains("CandidateList.ItemsSource = _decision.Candidates", code);
        Assert.DoesNotContain("ScanCandidates", code);
        Assert.DoesNotContain("Directory.GetFiles", code);
        Assert.DoesNotContain("Directory.EnumerateFiles", code);
        Assert.DoesNotContain("SqliteDatabaseHealthChecker", code);
    }

    [Fact]
    public void Corrupt_without_candidates_disables_recover()
    {
        var code = ReadCode();

        Assert.Contains("RecoverButton.IsEnabled = _decision.Candidates.Count > 0", code);
        Assert.Contains("if (_decision.Candidates.Count == 0)", code);
    }

    [Fact]
    public void Recover_returns_selected_candidate()
    {
        var code = ReadCode();

        Assert.Contains("_decision.Candidates[0]", code);
        Assert.Contains(
            "new DatabaseRecoveryDialogResult(DatabaseRecoveryDialogAction.Recover, _selectedCandidate)",
            code);
        Assert.Contains("Close();", code);
    }

    [Fact]
    public void Unavailable_only_offers_retry_open_and_exit()
    {
        var markup = ReadMarkup();
        var code = ReadCode();

        Assert.Contains("数据库当前不可访问", code);
        Assert.Contains("无法判断数据库是否损坏", code);
        Assert.Contains("ErrorType", markup);
        Assert.Contains("ErrorCode", markup);
        Assert.Contains("ErrorCodeName", markup);
        Assert.Contains("ErrorMessage", markup);
        Assert.Contains("SetUnavailableState", code);
        Assert.Contains("RecoverButton.Visibility = Visibility.Collapsed", code);
        Assert.Contains("ResumeButton.Visibility = Visibility.Collapsed", code);
        Assert.Contains("CandidateList.Visibility = Visibility.Collapsed", code);
    }

    [Fact]
    public void Unsupported_schema_shows_actual_and_supported_versions()
    {
        var markup = ReadMarkup();
        var code = ReadCode();
        var store = ReadSource("src", "MineRailMonitor.Infrastructure", "Persistence", "SqlitePassageRecordStore.cs");

        Assert.Contains("数据库版本高于当前应用支持版本，请升级应用程序", code);
        Assert.Contains("数据库实际版本", markup);
        Assert.Contains("当前应用支持版本", markup);
        Assert.Contains("health.SchemaVersion", code);
        Assert.Contains("SqlitePassageRecordStore.CurrentSchemaVersion", code);
        Assert.Contains("public const int CurrentSchemaVersion = 3", store);
        Assert.DoesNotContain("Text=\"3\"", markup);
    }

    [Fact]
    public void Unsupported_schema_has_no_recovery_action()
    {
        var code = ReadCode();

        Assert.Contains("SetUnsupportedSchemaState", code);
        Assert.Contains("RecoverButton.Visibility = Visibility.Collapsed", code);
        Assert.Contains("ResumeButton.Visibility = Visibility.Collapsed", code);
        Assert.Contains("CandidateList.Visibility = Visibility.Collapsed", code);
    }

    [Fact]
    public void Interrupted_recovery_displays_marker_evidence()
    {
        var markup = ReadMarkup();
        var code = ReadCode();

        Assert.Contains("上一次数据库恢复未完成", code);
        foreach (var field in new[]
                 {
                     "SourceBackupPath", "CorruptBundlePath", "StagingPath", "StartedAt",
                     "BundleManifestSha256"
                 })
        {
            Assert.Contains(field, markup);
            Assert.Contains($"marker.{field}", code);
        }
        Assert.Contains("系统检测到上一次恢复流程未完整结束", markup);
        Assert.Contains("在恢复完成前不会启动正常业务", markup);
    }

    [Fact]
    public void Interrupted_recovery_offers_resume_recovery()
    {
        var code = ReadCode();

        Assert.Contains("SetInterruptedRecoveryState", code);
        Assert.Contains(
            "new DatabaseRecoveryDialogResult(DatabaseRecoveryDialogAction.ResumeRecovery, null)",
            code);
        Assert.DoesNotContain("File.Exists", code);
        Assert.DoesNotContain("ValidateCorruptBundle", code);
    }

    [Fact]
    public void Recovery_state_error_never_offers_recover_or_resume()
    {
        var markup = ReadMarkup();
        var code = ReadCode();

        Assert.Contains("数据库恢复状态无法读取", code);
        Assert.Contains("SetRecoveryStateError", code);
        Assert.Contains("decision.ErrorMessage", code);
        Assert.Contains("RecoverButton.Visibility = Visibility.Collapsed", code);
        Assert.Contains("ResumeButton.Visibility = Visibility.Collapsed", code);
        Assert.DoesNotContain("Application.Current.Shutdown", code);
    }

    [Fact]
    public void Recovery_state_error_shows_error_message()
    {
        var markup = ReadMarkup();
        var code = ReadCode();

        Assert.Contains("无法确认上一次数据库恢复状态", markup);
        Assert.Contains("为避免覆盖现有数据，系统不会继续启动", markup);
        Assert.Contains("ErrorMessageText.Text = _decision.ErrorMessage", code);
    }

    [Fact]
    public void Retry_open_and_exit_only_return_actions()
    {
        var code = ReadCode();

        Assert.Contains("DatabaseRecoveryDialogAction.Retry", code);
        Assert.Contains("DatabaseRecoveryDialogAction.OpenDataDirectory", code);
        Assert.Contains("DatabaseRecoveryDialogAction.Exit", code);
        Assert.DoesNotContain("Process.Start", code);
        Assert.DoesNotContain("Application.Current.Shutdown", code);
        Assert.DoesNotContain("File.Copy", code);
        Assert.DoesNotContain("File.Move", code);
        Assert.DoesNotContain("File.Delete", code);
        Assert.DoesNotContain("SqliteRecoveryService", code);
    }

    [Fact]
    public void Recover_requires_admin_verification_prompt_but_does_not_authenticate_in_task_6()
    {
        var markup = ReadMarkup();
        var code = ReadCode();

        Assert.Contains("执行恢复前需要管理员验证", markup);
        Assert.DoesNotContain("AdminPasswordDialog", code);
        Assert.DoesNotContain("AdminModeService", code);
    }

    [Fact]
    public void Dialog_rejects_normal_startup_decisions()
    {
        var code = ReadCode();

        Assert.Contains("DatabaseStartupDecisionKind.CreateNew", code);
        Assert.Contains("DatabaseStartupDecisionKind.StartHealthy", code);
        Assert.Contains("throw new ArgumentException", code);
    }

    [Fact]
    public void Recovery_dialog_has_accessible_actions_and_wrapped_paths()
    {
        var markup = ReadMarkup();

        Assert.Contains("AutomationProperties.Name=\"恢复数据库\"", markup);
        Assert.Contains("AutomationProperties.Name=\"继续恢复\"", markup);
        Assert.Contains("AutomationProperties.Name=\"重试\"", markup);
        Assert.Contains("AutomationProperties.Name=\"打开数据目录\"", markup);
        Assert.Contains("AutomationProperties.Name=\"退出程序\"", markup);
        Assert.Contains("TextWrapping=\"Wrap\"", markup);
    }

    private static string ReadMarkup() => ReadSource("src", "MineRailMonitor", "Pages", "DatabaseRecoveryDialog.xaml");

    private static string ReadCode() => NormalizeNewlines(ReadSource("src", "MineRailMonitor", "Pages", "DatabaseRecoveryDialog.xaml.cs"));

    private static string ReadSource(params string[] segments) => File.ReadAllText(Locate(segments));

    private static string Locate(params string[] segments)
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            var candidate = Path.Combine(new[] { directory }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new FileNotFoundException($"Could not locate source file: {string.Join("/", segments)}");
    }

    private static string NormalizeNewlines(string value) => value.Replace("\r\n", "\n");
}
