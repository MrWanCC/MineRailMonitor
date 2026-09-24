namespace MineRailMonitor.Core.Tests;

public sealed class WindowChromeMarkupTests
{
    [Fact]
    public void MainWindow_defines_hit_testable_custom_window_commands()
    {
        var xaml = File.ReadAllText(LocateMainWindowXaml());

        Assert.Contains("WindowChrome.IsHitTestVisibleInChrome=\"True\"", xaml);
        Assert.Contains("x:Name=\"MinimizeWindowButton\"", xaml);
        Assert.Contains("x:Name=\"MaximizeWindowButton\"", xaml);
        Assert.Contains("x:Name=\"CloseWindowButton\"", xaml);
        Assert.Contains("Click=\"OnMinimizeClick\"", xaml);
        Assert.Contains("Click=\"OnMaximizeClick\"", xaml);
        Assert.Contains("Click=\"OnCloseClick\"", xaml);
        Assert.Contains("MinimizeIconGeometry", xaml);
        Assert.Contains("MaximizeIconGeometry", xaml);
        Assert.Contains("RestoreIconGeometry", xaml);
        Assert.Contains("CloseIconGeometry", xaml);
        Assert.Contains("Value=\"Maximized\"", xaml);
    }

    [Fact]
    public void MainWindow_keeps_the_reference_header_compact_without_clipping_clock_or_commands()
    {
        var xaml = File.ReadAllText(LocateMainWindowXaml());

        Assert.Contains("CaptionHeight=\"60\"", xaml);
        Assert.Contains("<RowDefinition Height=\"64\" />", xaml);
        Assert.Contains("<ColumnDefinition Width=\"220\" />", xaml);
        Assert.Contains("x:Name=\"HeaderDateSeparator\"", xaml);
        Assert.Contains("<RotateTransform Angle=\"28\" />", xaml);
        Assert.Contains("x:Name=\"HeaderSystemStatusDot\"", xaml);
        Assert.Contains("x:Name=\"HeaderRfidStatusDot\"", xaml);
        Assert.Contains("x:Name=\"HeaderExternalStatusDot\"", xaml);
        Assert.Contains("Text=\"外部接口未配置\"", xaml);
        Assert.DoesNotContain("系统正常 · 演示模式", xaml);
        Assert.DoesNotContain("读卡分站 · 未接入", xaml);

        var code = File.ReadAllText(LocateMainWindowCode());
        Assert.DoesNotContain("读卡分站 ·", code);
        Assert.Contains("UpdateHeaderStatusIndicators", code);
    }

    [Fact]
    public void MainWindow_uses_the_monitor_work_area_for_maximized_bounds()
    {
        var code = File.ReadAllText(LocateMainWindowCode());

        Assert.Contains("WM_GETMINMAXINFO", code);
        Assert.Contains("GetMonitorInfo", code);
        Assert.Contains("rcWork", code);
    }

    [Fact]
    public void SettingsPage_exposes_an_administrator_mode_entry()
    {
        var xaml = File.ReadAllText(LocateSourceFile("Pages", "SettingsPage.xaml"));

        Assert.Contains("进入管理员模式", xaml);
        Assert.Contains("Click=\"OnEnterAdminModeClick\"", xaml);
    }

    [Fact]
    public void Administrator_password_dialog_contains_the_required_controls_and_error_copy()
    {
        var dialogPath = LocateSourceFile("Pages", "AdminPasswordDialog.xaml");

        Assert.True(File.Exists(dialogPath), "管理员验证对话框 XAML 应存在。");

        var xaml = File.ReadAllText(dialogPath);
        Assert.Contains("管理员验证", xaml);
        Assert.Contains("密码", xaml);
        Assert.Contains("PasswordBox", xaml);
        Assert.Contains("确认", xaml);
        Assert.Contains("取消", xaml);
        Assert.Contains("管理员密码错误", xaml);
    }

    [Fact]
    public void Administrator_password_dialog_uses_the_dark_application_theme()
    {
        var xaml = File.ReadAllText(LocateSourceFile("Pages", "AdminPasswordDialog.xaml"));

        Assert.Contains("Background=\"{StaticResource CardBackgroundBrush}\"", xaml);
        Assert.Contains("Foreground=\"{StaticResource TextPrimaryBrush}\"", xaml);
    }

    [Fact]
    public void Administrator_password_dialog_uses_a_custom_dark_title_bar()
    {
        var xaml = File.ReadAllText(LocateSourceFile("Pages", "AdminPasswordDialog.xaml"));
        var code = File.ReadAllText(LocateSourceFile("Pages", "AdminPasswordDialog.xaml.cs"));

        Assert.Contains("WindowStyle=\"None\"", xaml);
        Assert.Contains("AllowsTransparency=\"True\"", xaml);
        Assert.Contains("MouseLeftButtonDown=\"OnHeaderMouseLeftButtonDown\"", xaml);
        Assert.Contains("Click=\"OnCloseClick\"", xaml);
        Assert.Contains("OnHeaderMouseLeftButtonDown", code);
        Assert.Contains("DragMove", code);
    }

    [Fact]
    public void MainWindow_exposes_an_administrator_banner_and_exit_entry()
    {
        var xaml = File.ReadAllText(LocateMainWindowXaml());
        var code = File.ReadAllText(LocateMainWindowCode());

        Assert.Contains("管理员模式 · 地图可编辑", xaml);
        Assert.Contains("退出管理员模式", xaml);
        Assert.Contains("Click=\"OnExitAdminModeClick\"", xaml);
        Assert.Contains("OnExitAdminModeClick", code);
        Assert.Contains("ExitAdminMode()", code);
        Assert.Contains("_settingsPage.TryLeaveAsync", code);
    }

    [Fact]
    public void MainWindow_does_not_reserve_sidebar_space_for_a_version_footer()
    {
        var xaml = File.ReadAllText(LocateMainWindowXaml());
        var code = File.ReadAllText(LocateMainWindowCode());

        Assert.DoesNotContain("x:Name=\"SidebarVersionPanel\"", xaml);
        Assert.DoesNotContain("Text=\"v1.0.0\"", xaml);
        Assert.DoesNotContain("public string DisplayVersion", code);
        Assert.DoesNotContain("typeof(MainWindow).Assembly.GetName().Version", code);
    }

    [Fact]
    public void Unsaved_map_changes_dialog_exposes_the_three_exit_choices()
    {
        var xaml = File.ReadAllText(LocateSourceFile("Pages", "UnsavedMapChangesDialog.xaml"));
        var code = File.ReadAllText(LocateSourceFile("Pages", "UnsavedMapChangesDialog.xaml.cs"));

        Assert.Contains("地图标注存在未保存修改。", xaml);
        Assert.Contains("保存并退出", xaml);
        Assert.Contains("放弃修改", xaml);
        Assert.Contains("取消", xaml);
        Assert.Contains("UnsavedMapChangesResult.SaveAndExit", code);
        Assert.Contains("UnsavedMapChangesResult.Discard", code);
        Assert.Contains("UnsavedMapChangesResult.Cancel", code);
    }

    [Fact]
    public void MainWindow_checks_unsaved_settings_before_navigation_and_close()
    {
        var code = File.ReadAllText(LocateMainWindowCode());

        Assert.Contains("_settingsPage.TryLeaveAsync", code);
        Assert.Contains("关闭程序", code);
        Assert.Contains("离开系统设置", code);
    }

    [Fact]
    public void Delete_map_annotation_dialog_uses_the_application_theme()
    {
        var dialogPath = LocateSourceFile("Pages", "DeleteMapAnnotationDialog.xaml");
        var dialogCodePath = LocateSourceFile("Pages", "DeleteMapAnnotationDialog.xaml.cs");
        var monitorCode = File.ReadAllText(LocateSourceFile("Pages", "MonitorPage.xaml.cs"));

        Assert.True(File.Exists(dialogPath), "删除地图标注确认窗口 XAML 应存在。");

        var xaml = File.ReadAllText(dialogPath);
        var code = File.ReadAllText(dialogCodePath);
        Assert.Contains("WindowStyle=\"None\"", xaml);
        Assert.Contains("CardBackgroundBrush", xaml);
        Assert.Contains("TextPrimaryBrush", xaml);
        Assert.Contains("确认删除地图标注", xaml);
        Assert.Contains("取消", xaml);
        Assert.Contains("删除", xaml);
        Assert.Contains("DialogResult = true", code);
        Assert.Contains("DragMove", code);
        Assert.Contains("DeleteMapAnnotationDialog", monitorCode);
        Assert.DoesNotContain("MessageBox.Show(Window.GetWindow(this), $\"确定删除", monitorCode);
    }

    [Fact]
    public void Shared_form_controls_keep_settings_inputs_and_buttons_readable()
    {
        var styles = File.ReadAllText(LocateSourceFile("Styles", "Cards.xaml"));

        Assert.Contains("<Style TargetType=\"TextBox\">", styles);
        Assert.Contains("<Style TargetType=\"PasswordBox\">", styles);
        Assert.Contains("<Style TargetType=\"Button\">", styles);
        Assert.Contains("#0B2A43", styles);
        Assert.Contains("TextPrimaryBrush", styles);
        Assert.Contains("ControlTemplate TargetType=\"Button\"", styles);
    }

    private static string LocateMainWindowXaml()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "MineRailMonitor", "MainWindow.xaml");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Unable to locate MainWindow.xaml from the test directory.");
    }

    private static string LocateMainWindowCode()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "MineRailMonitor", "MainWindow.xaml.cs");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Unable to locate MainWindow.xaml.cs from the test directory.");
    }

    private static string LocateSourceFile(params string[] pathParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var sourceRoot = Path.Combine(directory.FullName, "src", "MineRailMonitor");
            if (Directory.Exists(sourceRoot))
            {
                return Path.Combine(new[] { sourceRoot }.Concat(pathParts).ToArray());
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Unable to locate {Path.Combine(pathParts)} from the test directory.");
    }
}
