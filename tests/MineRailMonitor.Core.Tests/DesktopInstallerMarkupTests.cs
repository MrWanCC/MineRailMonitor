namespace MineRailMonitor.Core.Tests;

public sealed class DesktopInstallerMarkupTests
{
    [Fact]
    public void Installer_uses_C_default_and_selected_directory_as_Root()
    {
        var script = ReadSource("installer", "MineRailMonitor.iss");

        Assert.Contains("DefaultDirName=C:\\MineRailMonitor", script, StringComparison.Ordinal);
        Assert.Contains("{app}\\App\\MineRailMonitor.exe", script, StringComparison.Ordinal);
        Assert.DoesNotContain("{app}\\MineRailMonitor", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Installer_deploys_App_and_root_level_Projects()
    {
        var script = ReadSource("installer", "MineRailMonitor.iss");

        Assert.Contains("desktop-package\\App", script, StringComparison.Ordinal);
        Assert.Contains("{app}\\App", script, StringComparison.Ordinal);
        Assert.Contains("desktop-package\\Projects", script, StringComparison.Ordinal);
        Assert.Contains("{app}\\Projects", script, StringComparison.Ordinal);
        Assert.Contains("onlyifdoesntexist", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Installer_preserves_existing_site_config_on_upgrade()
    {
        var script = ReadSource("installer", "MineRailMonitor.iss");

        Assert.Contains(
            "Source: \"..\\artifacts\\desktop-package\\App\\*\"; Excludes: \"MineRailMonitor.exe.config\";",
            script,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Source: \"..\\artifacts\\desktop-package\\App\\*\"; DestDir: \"{app}\\App\";",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "Source: \"..\\artifacts\\desktop-package\\App\\MineRailMonitor.exe.config\"",
            script,
            StringComparison.Ordinal);
        Assert.Contains("onlyifdoesntexist", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Installer_creates_only_the_documented_desktop_shortcut()
    {
        var script = ReadSource("installer", "MineRailMonitor.iss");

        Assert.Contains("矿车编组监控系统", script, StringComparison.Ordinal);
        Assert.Contains("{autodesktop}", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Service", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Registry Run", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Installer_uses_stable_AppId_and_checks_DotNet_Framework_48()
    {
        var script = ReadSource("installer", "MineRailMonitor.iss");

        Assert.Contains("AppId={{8C8B1CB5-4A4B-4B4A-9D48-6A3C93D2F0E1}", script, StringComparison.Ordinal);
        Assert.Contains("ArchitecturesInstallIn64BitMode=x64os", script, StringComparison.Ordinal);
        var architectureLines = script.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        Assert.DoesNotContain("ArchitecturesInstallIn64BitMode=x64", architectureLines);
        Assert.Contains("InitializeSetup", script, StringComparison.Ordinal);
        Assert.Contains("NET Framework Setup\\NDP\\v4\\Full", script, StringComparison.Ordinal);
        Assert.Contains("Release", script, StringComparison.Ordinal);
        Assert.Contains("528040", script, StringComparison.Ordinal);
        Assert.Contains("请先安装 .NET Framework 4.8", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Installer_grants_Modify_only_to_runtime_data_directories()
    {
        var script = ReadSource("installer", "MineRailMonitor.iss");

        Assert.Contains("icacls.exe", script, StringComparison.Ordinal);
        Assert.Contains("/reset", script, StringComparison.Ordinal);
        Assert.Contains("/reset /T /C", script, StringComparison.Ordinal);
        Assert.Contains("/inheritance:r", script, StringComparison.Ordinal);
        Assert.Contains("*S-1-5-18", script, StringComparison.Ordinal);
        Assert.Contains("*S-1-5-32-544", script, StringComparison.Ordinal);
        Assert.Contains("*S-1-5-32-545", script, StringComparison.Ordinal);
        Assert.Contains("(OI)(CI)(RX)", script, StringComparison.Ordinal);
        Assert.Contains("(OI)(CI)(M)", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Permissions: users-modify", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Installer_reuses_previous_Root_on_upgrade()
    {
        var script = ReadSource("installer", "MineRailMonitor.iss");

        Assert.Contains("UsePreviousAppDir=yes", script, StringComparison.Ordinal);
        Assert.Contains("DefaultDirName=C:\\MineRailMonitor", script, StringComparison.Ordinal);
        Assert.Contains("当前版本不支持升级时迁移安装目录", script, StringComparison.Ordinal);
        Assert.Contains("Result := False", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Installer_has_final_preinstall_cross_root_guard()
    {
        var script = ReadSource("installer", "MineRailMonitor.iss");

        Assert.Contains("PrepareToInstall", script, StringComparison.Ordinal);
        Assert.Contains("当前版本不支持升级时迁移安装目录", script, StringComparison.Ordinal);
        Assert.Contains("当前版本不支持升级时迁移安装目录，请继续使用原安装目录。", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Uninstall_keeps_field_data_by_default_and_requires_explicit_confirmation()
    {
        var script = ReadSource("installer", "MineRailMonitor.iss");

        Assert.Contains("[UninstallDelete]", script, StringComparison.Ordinal);
        Assert.Contains("{app}\\App", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Type: filesandordirs; Name: \"{app}\\Data\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Type: filesandordirs; Name: \"{app}\\Projects\"", script, StringComparison.Ordinal);
        Assert.Contains("InitializeUninstall", script, StringComparison.Ordinal);
        Assert.Contains("是否同时删除现场数据和历史记录", script, StringComparison.Ordinal);
        Assert.Contains("将删除 Projects、Data、Backups、Logs 和 Docs 中的现场文件", script, StringComparison.Ordinal);
        Assert.Contains("DeleteFieldData := False", script, StringComparison.Ordinal);
        Assert.Contains("DelTree(ExpandConstant('{app}\\Data')", script, StringComparison.Ordinal);
        Assert.Contains("DelTree(ExpandConstant('{app}\\Projects')", script, StringComparison.Ordinal);
        Assert.Contains("DelTree(ExpandConstant('{app}\\Docs')", script, StringComparison.Ordinal);
        Assert.DoesNotContain("DelTree(ExpandConstant('{app}')", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Silent_uninstall_keeps_field_data_by_default()
    {
        var script = ReadSource("installer", "MineRailMonitor.iss");

        Assert.Contains("GetCmdTail", script, StringComparison.Ordinal);
        Assert.Contains("'/VERYSILENT'", script, StringComparison.Ordinal);
        Assert.Contains("'/SILENT'", script, StringComparison.Ordinal);
        Assert.Contains("DeleteFieldData := False", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Installer_rejects_volume_root_before_acl_changes()
    {
        var script = ReadSource("installer", "MineRailMonitor.iss");

        Assert.Contains("function IsDriveRoot", script, StringComparison.Ordinal);
        Assert.Contains("function ValidateInstallRoot", script, StringComparison.Ordinal);
        Assert.Contains("ExpandConstant('{app}')", script, StringComparison.Ordinal);
        Assert.Contains("ValidateInstallRoot", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Installer_rejects_system_and_program_files_roots()
    {
        var script = ReadSource("installer", "MineRailMonitor.iss");

        Assert.Contains("ExpandConstant('{win}')", script, StringComparison.Ordinal);
        Assert.Contains("ExpandConstant('{sys}')", script, StringComparison.Ordinal);
        Assert.Contains("ExpandConstant('{pf}')", script, StringComparison.Ordinal);
        Assert.Contains("ExpandConstant('{pf32}')", script, StringComparison.Ordinal);
        Assert.Contains("ExpandConstant('{pf64}')", script, StringComparison.Ordinal);
        Assert.Contains("IsPathEqualOrBelow", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Installer_rejects_unrelated_nonempty_first_install_root()
    {
        var script = ReadSource("installer", "MineRailMonitor.iss");

        Assert.Contains("ContainsOnlyRetainedMineRailData", script, StringComparison.Ordinal);
        Assert.Contains("FindFirst", script, StringComparison.Ordinal);
        Assert.Contains("请选择一个 MineRailMonitor 专用安装目录", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Installer_allows_empty_precreated_root()
    {
        var script = ReadSource("installer", "MineRailMonitor.iss");

        Assert.Contains("if not DirExists", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Installer_allows_retained_field_data_root_for_reinstall()
    {
        var script = ReadSource("installer", "MineRailMonitor.iss");

        Assert.Contains("Projects", script, StringComparison.Ordinal);
        Assert.Contains("Data", script, StringComparison.Ordinal);
        Assert.Contains("Backups", script, StringComparison.Ordinal);
        Assert.Contains("Logs", script, StringComparison.Ordinal);
        Assert.Contains("Docs", script, StringComparison.Ordinal);
        Assert.Contains("IsRetainedFieldDataDirectory", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Installer_runs_root_validation_from_PrepareToInstall()
    {
        var script = ReadSource("installer", "MineRailMonitor.iss");
        var prepareToInstall = script.IndexOf("function PrepareToInstall", StringComparison.Ordinal);
        var prepareEnd = script.IndexOf("function IsSilentUninstall", prepareToInstall, StringComparison.Ordinal);

        Assert.True(prepareToInstall >= 0);
        Assert.True(prepareEnd > prepareToInstall);
        Assert.Contains(
            "ValidateInstallRoot(ExpandConstant('{app}'), PreviousRoot)",
            script.Substring(prepareToInstall, prepareEnd - prepareToInstall),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Uninstall_confirmation_defaults_to_No()
    {
        var script = ReadSource("installer", "MineRailMonitor.iss");

        var defaultNoCount = script.Split(
            new[] { "MB_YESNO or MB_DEFBUTTON2" },
            StringSplitOptions.None).Length - 1;

        Assert.Equal(2, defaultNoCount);
    }

    [Fact]
    public void Installer_requires_retained_entries_to_be_real_directories()
    {
        var script = ReadSource("installer", "MineRailMonitor.iss");

        Assert.Contains("function IsSafeRetainedFieldDataEntry", script, StringComparison.Ordinal);
        Assert.Contains("FILE_ATTRIBUTE_DIRECTORY", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Installer_rejects_reparse_points_in_retained_root()
    {
        var script = ReadSource("installer", "MineRailMonitor.iss");

        Assert.Contains("FILE_ATTRIBUTE_REPARSE_POINT", script, StringComparison.Ordinal);
        Assert.Contains("IsSafeRetainedFieldDataEntry(FindData)", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Installer_rejects_reparse_point_on_selected_root()
    {
        var script = ReadSource("installer", "MineRailMonitor.iss");

        Assert.Contains("function ValidateRootPathChain", script, StringComparison.Ordinal);
        Assert.Contains("FILE_ATTRIBUTE_REPARSE_POINT", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Installer_rejects_reparse_point_in_existing_root_ancestor()
    {
        var script = ReadSource("installer", "MineRailMonitor.iss");

        Assert.Contains("function ValidateRootPathChain", script, StringComparison.Ordinal);
        Assert.Contains("ExtractFileDir", script, StringComparison.Ordinal);
        Assert.Contains("FindFirst", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Installer_scans_managed_subtrees_for_reparse_points_before_acl()
    {
        var script = ReadSource("installer", "MineRailMonitor.iss");
        var prepareToInstall = script.IndexOf("function PrepareToInstall", StringComparison.Ordinal);
        var prepareEnd = script.IndexOf("function IsSilentUninstall", prepareToInstall, StringComparison.Ordinal);

        Assert.True(prepareToInstall >= 0);
        Assert.True(prepareEnd > prepareToInstall);

        var prepareBody = script.Substring(prepareToInstall, prepareEnd - prepareToInstall);
        Assert.Contains(
            "ValidateInstallRoot(ExpandConstant('{app}'), PreviousRoot)",
            prepareBody,
            StringComparison.Ordinal);
        Assert.Contains("ApplyMineRailMonitorAcl", script, StringComparison.Ordinal);

        var validateInstallRoot = script.IndexOf("function ValidateInstallRoot", StringComparison.Ordinal);
        var validatePreviousRoot = script.IndexOf("function ValidatePreviousRoot", validateInstallRoot, StringComparison.Ordinal);
        Assert.True(validateInstallRoot >= 0);
        Assert.True(validatePreviousRoot > validateInstallRoot);
        Assert.Contains(
            "ScanManagedTreeForReparsePoints",
            script.Substring(validateInstallRoot, validatePreviousRoot - validateInstallRoot),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Installer_scans_reparse_points_even_on_same_root_upgrade()
    {
        var script = ReadSource("installer", "MineRailMonitor.iss");
        var validateInstallRoot = script.IndexOf("function ValidateInstallRoot", StringComparison.Ordinal);
        var validatePreviousRoot = script.IndexOf("function ValidatePreviousRoot", validateInstallRoot, StringComparison.Ordinal);

        Assert.True(validateInstallRoot >= 0);
        Assert.True(validatePreviousRoot > validateInstallRoot);

        var validationBody = script.Substring(validateInstallRoot, validatePreviousRoot - validateInstallRoot);
        Assert.Contains("ScanManagedTreeForReparsePoints", validationBody, StringComparison.Ordinal);
        Assert.Contains("PreviousRoot", validationBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Installer_never_recurses_into_detected_reparse_point()
    {
        var script = ReadSource("installer", "MineRailMonitor.iss");

        Assert.Contains("function IsReparsePoint", script, StringComparison.Ordinal);
        Assert.Contains("if IsReparsePoint(FindData) then", script, StringComparison.Ordinal);
        Assert.Contains("FILE_ATTRIBUTE_DIRECTORY", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Installer_disallows_UNC_and_network_roots()
    {
        var script = ReadSource("installer", "MineRailMonitor.iss");

        Assert.Contains("AllowUNCPath=no", script, StringComparison.Ordinal);
        Assert.Contains("AllowNetworkDrive=no", script, StringComparison.Ordinal);
        Assert.Contains("ExpandUNCFileName", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Installer_explicitly_allows_only_x64_os()
    {
        var script = ReadSource("installer", "MineRailMonitor.iss");
        var architectureLines = script.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        Assert.Contains("ArchitecturesAllowed=x64os", architectureLines);
        Assert.Contains("ArchitecturesInstallIn64BitMode=x64os", architectureLines);
    }

    private static string ReadSource(params string[] segments) =>
        File.ReadAllText(Locate(segments));

    private static string Locate(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate source file: {Path.Combine(segments)}");
    }
}
