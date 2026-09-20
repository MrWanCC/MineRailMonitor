using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MineRailMonitor.Infrastructure.Persistence;

namespace MineRailMonitor.Pages;

public enum DatabaseRecoveryDialogAction
{
    Recover,
    ResumeRecovery,
    Retry,
    OpenDataDirectory,
    Exit
}

public sealed class DatabaseRecoveryDialogResult
{
    public DatabaseRecoveryDialogResult(DatabaseRecoveryDialogAction action, SqliteBackupCandidate? candidate)
    {
        Action = action;
        Candidate = candidate;
    }

    public DatabaseRecoveryDialogAction Action { get; }

    public SqliteBackupCandidate? Candidate { get; }
}

public sealed partial class DatabaseRecoveryDialog : Window
{
    private readonly DatabaseStartupDecision _decision;
    private SqliteBackupCandidate? _selectedCandidate;
    private bool _actionSelected;
    private DatabaseRecoveryDialogResult _result = new(DatabaseRecoveryDialogAction.Exit, null);

    public DatabaseRecoveryDialog(DatabaseStartupDecision decision)
    {
        if (decision is null)
        {
            throw new ArgumentNullException(nameof(decision));
        }

        if (decision.Kind is DatabaseStartupDecisionKind.CreateNew or DatabaseStartupDecisionKind.StartHealthy)
        {
            throw new ArgumentException("正常启动决策不应显示恢复窗口。", nameof(decision));
        }

        _decision = decision;
        InitializeComponent();
        InitializeDecision();
    }

    public DatabaseRecoveryDialogResult Result => _result;

    private void InitializeDecision()
    {
        HideOptionalSections();

        switch (_decision.Kind)
        {
            case DatabaseStartupDecisionKind.RecoverCorrupt:
                SetCorruptState();
                break;
            case DatabaseStartupDecisionKind.Unavailable:
                SetUnavailableState();
                break;
            case DatabaseStartupDecisionKind.UnsupportedSchema:
                SetUnsupportedSchemaState();
                break;
            case DatabaseStartupDecisionKind.InterruptedRecovery:
                SetInterruptedRecoveryState();
                break;
            case DatabaseStartupDecisionKind.RecoveryStateError:
                SetRecoveryStateError();
                break;
            default:
                throw new ArgumentException("该启动决策不属于恢复错误窗口。", nameof(_decision));
        }
    }

    private void SetCorruptState()
    {
        var health = RequireHealth();
        StateTitleText.Text = "数据库完整性异常";
        StateSummaryText.Text = "系统检测到生产数据库存在完整性问题，请选择已验证的备份。";
        PopulateHealthSummary(health);
        RecoveryWarningBorder.Visibility = Visibility.Visible;
        CandidatePanel.Visibility = Visibility.Visible;
        CandidateList.ItemsSource = _decision.Candidates;
        _selectedCandidate = _decision.Candidates.Count > 0 ? _decision.Candidates[0] : null;
        CandidateList.SelectedIndex = _selectedCandidate is null ? -1 : 0;
        NoCandidateText.Visibility = _selectedCandidate is null ? Visibility.Visible : Visibility.Collapsed;
        RecoverButton.IsEnabled = _decision.Candidates.Count > 0;
        if (_decision.Candidates.Count == 0)
        {
            RecoverButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            RecoverButton.Visibility = Visibility.Visible;
        }
        ResumeButton.Visibility = Visibility.Collapsed;
        AdminVerificationHint.Visibility = Visibility.Visible;
    }

    private void SetUnavailableState()
    {
        StateTitleText.Text = "数据库当前不可访问";
        StateSummaryText.Text = "无法判断数据库是否损坏。请检查文件权限、磁盘和数据库占用后重试。";
        if (_decision.Health is not null)
        {
            PopulateHealthSummary(_decision.Health);
            HealthSummaryPanel.Visibility = Visibility.Visible;
        }
        CandidateList.Visibility = Visibility.Collapsed;
        CandidatePanel.Visibility = Visibility.Collapsed;
        RecoverButton.Visibility = Visibility.Collapsed;
        ResumeButton.Visibility = Visibility.Collapsed;
        AdminVerificationHint.Visibility = Visibility.Collapsed;
    }

    private void SetUnsupportedSchemaState()
    {
        var health = RequireHealth();
        StateTitleText.Text = "数据库版本不兼容";
        StateSummaryText.Text = "数据库版本高于当前应用支持版本，请升级应用程序。";
        ActualSchemaVersionText.Text = health.SchemaVersion?.ToString(CultureInfo.InvariantCulture) ?? "-";
        SupportedSchemaVersionText.Text = SqlitePassageRecordStore.CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture);
        SchemaPanel.Visibility = Visibility.Visible;
        HealthSummaryPanel.Visibility = Visibility.Collapsed;
        CandidatePanel.Visibility = Visibility.Collapsed;
        RecoverButton.Visibility = Visibility.Collapsed;
        ResumeButton.Visibility = Visibility.Collapsed;
        AdminVerificationHint.Visibility = Visibility.Collapsed;
    }

    private void SetInterruptedRecoveryState()
    {
        var marker = _decision.RecoveryMarker ?? throw new ArgumentException("InterruptedRecovery 必须携带 marker。", nameof(_decision));
        StateTitleText.Text = "上一次数据库恢复未完成";
        StateSummaryText.Text = "系统检测到上一次恢复流程未完整结束。在恢复完成前不会启动正常业务。";
        SourceBackupPathText.Text = marker.SourceBackupPath;
        CorruptBundlePathText.Text = marker.CorruptBundlePath;
        StagingPathText.Text = marker.StagingPath;
        StartedAtText.Text = marker.StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        BundleManifestSha256Text.Text = marker.BundleManifestSha256;
        InterruptedPanel.Visibility = Visibility.Visible;
        RecoverButton.Visibility = Visibility.Collapsed;
        ResumeButton.Visibility = Visibility.Visible;
        AdminVerificationHint.Visibility = Visibility.Visible;
    }

    private void SetRecoveryStateError()
    {
        StateTitleText.Text = "数据库恢复状态无法读取";
        StateSummaryText.Text = _decision.ErrorMessage ?? "无法读取数据库恢复状态。";
        ErrorMessageText.Text = _decision.ErrorMessage ?? "-";
        RecoveryStateErrorPanel.Visibility = Visibility.Visible;
        HealthSummaryPanel.Visibility = Visibility.Collapsed;
        CandidatePanel.Visibility = Visibility.Collapsed;
        RecoverButton.Visibility = Visibility.Collapsed;
        ResumeButton.Visibility = Visibility.Collapsed;
        AdminVerificationHint.Visibility = Visibility.Collapsed;
    }

    private void PopulateHealthSummary(SqliteDatabaseHealthResult health)
    {
        DatabasePathText.Text = health.DatabasePath;
        QuickCheckText.Text = FormatCheck(health.QuickCheckPassed, health.QuickCheckSummary);
        IntegrityCheckText.Text = health.IntegrityCheckExecuted
            ? FormatCheck(health.IntegrityCheckPassed, health.IntegrityCheckSummary)
            : "未执行";
        ForeignKeyCheckText.Text = FormatCheck(health.ForeignKeyCheckPassed, health.ForeignKeyCheckSummary);
        ErrorTypeText.Text = health.ErrorType ?? "-";
        ErrorCodeText.Text = health.ErrorCode?.ToString(CultureInfo.InvariantCulture) ?? "-";
        ErrorCodeNameText.Text = health.ErrorCodeName ?? "-";
        ErrorMessageText.Text = health.ErrorMessage ?? "-";
        HealthSummaryPanel.Visibility = Visibility.Visible;
    }

    private void HideOptionalSections()
    {
        RecoveryWarningBorder.Visibility = Visibility.Collapsed;
        HealthSummaryPanel.Visibility = Visibility.Collapsed;
        CandidatePanel.Visibility = Visibility.Collapsed;
        SchemaPanel.Visibility = Visibility.Collapsed;
        InterruptedPanel.Visibility = Visibility.Collapsed;
        RecoveryStateErrorPanel.Visibility = Visibility.Collapsed;
        NoCandidateText.Visibility = Visibility.Collapsed;
        RecoverButton.Visibility = Visibility.Visible;
        ResumeButton.Visibility = Visibility.Visible;
        AdminVerificationHint.Visibility = Visibility.Visible;
    }

    private void OnCandidateSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedCandidate = CandidateList.SelectedItem as SqliteBackupCandidate;
    }

    private void OnRecoverClick(object sender, RoutedEventArgs e)
    {
        if (_selectedCandidate is null)
        {
            return;
        }

        Complete(new DatabaseRecoveryDialogResult(DatabaseRecoveryDialogAction.Recover, _selectedCandidate));
    }

    private void OnResumeClick(object sender, RoutedEventArgs e)
    {
        Complete(new DatabaseRecoveryDialogResult(DatabaseRecoveryDialogAction.ResumeRecovery, null));
    }

    private void OnRetryClick(object sender, RoutedEventArgs e)
    {
        Complete(new DatabaseRecoveryDialogResult(DatabaseRecoveryDialogAction.Retry, null));
    }

    private void OnOpenDataDirectoryClick(object sender, RoutedEventArgs e)
    {
        Complete(new DatabaseRecoveryDialogResult(DatabaseRecoveryDialogAction.OpenDataDirectory, null));
    }

    private void OnExitClick(object sender, RoutedEventArgs e)
    {
        Complete(new DatabaseRecoveryDialogResult(DatabaseRecoveryDialogAction.Exit, null));
    }

    private void Complete(DatabaseRecoveryDialogResult result)
    {
        _result = result;
        _actionSelected = true;
        Close();
    }

    private void OnDialogClosing(object? sender, CancelEventArgs e)
    {
        if (!_actionSelected)
        {
            _result = new DatabaseRecoveryDialogResult(DatabaseRecoveryDialogAction.Exit, null);
        }
    }

    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private SqliteDatabaseHealthResult RequireHealth() =>
        _decision.Health ?? throw new ArgumentException("该启动决策缺少健康检查结果。", nameof(_decision));

    private static string FormatCheck(bool passed, string summary) =>
        passed ? $"通过：{summary}" : $"异常：{summary}";
}
