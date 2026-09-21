#define MyAppName "MineRailMonitor"
#define MyAppVersion "1.0.0"

[Setup]
AppId={{8C8B1CB5-4A4B-4B4A-9D48-6A3C93D2F0E1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName=C:\MineRailMonitor
UsePreviousAppDir=yes
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64os
OutputDir=..\artifacts\installer
OutputBaseFilename=MineRailMonitor-Setup
DisableProgramGroupPage=yes
Uninstallable=yes

[Files]
Source: "..\artifacts\desktop-package\App\*"; Excludes: "MineRailMonitor.exe.config"; DestDir: "{app}\App"; Flags: recursesubdirs createallsubdirs ignoreversion
Source: "..\artifacts\desktop-package\App\MineRailMonitor.exe.config"; DestDir: "{app}\App"; Flags: onlyifdoesntexist ignoreversion
Source: "..\artifacts\desktop-package\Projects\*"; DestDir: "{app}\Projects"; Flags: recursesubdirs createallsubdirs onlyifdoesntexist
Source: "..\artifacts\desktop-package\Docs\*"; DestDir: "{app}\Docs"; Flags: recursesubdirs createallsubdirs onlyifdoesntexist skipifsourcedoesntexist

[Dirs]
Name: "{app}"
Name: "{app}\App"
Name: "{app}\Data"
Name: "{app}\Backups\SQLite"
Name: "{app}\Logs\BlackBox"
Name: "{app}\Projects"
Name: "{app}\Docs"

[Icons]
Name: "{autodesktop}\矿车编组监控系统"; Filename: "{app}\App\MineRailMonitor.exe"

[Run]
Filename: "{app}\App\MineRailMonitor.exe"; Description: "立即启动 MineRailMonitor"; Flags: postinstall nowait skipifsilent unchecked

[UninstallDelete]
Type: filesandordirs; Name: "{app}\App"

[Registry]
Root: HKLM; Subkey: "Software\MineRailMonitor"; ValueType: string; ValueName: "InstallLocation"; ValueData: "{app}"; Flags: uninsdeletekeyifempty

[Code]
const
  DotNet48MinimumRelease = 528040;
  SidSystem = '*S-1-5-18';
  SidAdministrators = '*S-1-5-32-544';
  SidUsers = '*S-1-5-32-545';
  UsersReadExecuteRights = '(OI)(CI)(RX)';
  UsersModifyRights = '(OI)(CI)(M)';

var
  DeleteFieldData: Boolean;

function HasDotNet48(): Boolean;
var
  Release: Cardinal;
begin
  Result := False;
  if IsWin64 then
    Result := RegQueryDWordValue(HKLM64,
      'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full',
      'Release', Release) and (Release >= DotNet48MinimumRelease);
  if not Result then
    Result := RegQueryDWordValue(HKLM,
      'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full',
      'Release', Release) and (Release >= DotNet48MinimumRelease);
end;

function InitializeSetup(): Boolean;
begin
  Result := HasDotNet48;
  if not Result then
    MsgBox('本机未检测到 .NET Framework 4.8，请先安装 .NET Framework 4.8。',
      mbCriticalError, MB_OK);
end;

function RunIcacls(const Parameters: String): Boolean;
var
  ResultCode: Integer;
begin
  if not Exec(ExpandConstant('{sys}\icacls.exe'), Parameters, '',
    SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Result := False
  else
    Result := ResultCode = 0;
end;

function SetEffectiveAcl(const DirectoryName, UserRights: String;
  ResetTree: Boolean): Boolean;
var
  ResetParameters: String;
  Parameters: String;
begin
  if ResetTree then
    ResetParameters := '"' + DirectoryName + '" /reset /T /C'
  else
    ResetParameters := '"' + DirectoryName + '" /reset';
  if not RunIcacls(ResetParameters) then begin
    Result := False;
    exit;
  end;

  if not RunIcacls('"' + DirectoryName + '" /inheritance:r') then begin
    Result := False;
    exit;
  end;

  Parameters := '"' + DirectoryName + '" /grant:r ' +
    '"' + SidSystem + ':(OI)(CI)(F)" ' +
    '"' + SidAdministrators + ':(OI)(CI)(F)" ' +
    '"' + SidUsers + ':' + UserRights + '"';
  Result := RunIcacls(Parameters);
end;

function ApplyMineRailMonitorAcl(): Boolean;
begin
  Result :=
    SetEffectiveAcl(ExpandConstant('{app}'), UsersReadExecuteRights, False) and
    SetEffectiveAcl(ExpandConstant('{app}\App'), UsersReadExecuteRights, True) and
    SetEffectiveAcl(ExpandConstant('{app}\Data'), UsersModifyRights, True) and
    SetEffectiveAcl(ExpandConstant('{app}\Backups'), UsersModifyRights, True) and
    SetEffectiveAcl(ExpandConstant('{app}\Logs'), UsersModifyRights, True) and
    SetEffectiveAcl(ExpandConstant('{app}\Projects'), UsersModifyRights, True) and
    SetEffectiveAcl(ExpandConstant('{app}\Docs'), UsersModifyRights, True);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and not ApplyMineRailMonitorAcl then begin
    MsgBox('无法规范化 MineRailMonitor 目录权限，安装将停止。',
      mbCriticalError, MB_OK);
    Abort;
  end;
end;

function NormalizeRoot(const DirectoryName: String): String;
begin
  Result := DirectoryName;
  while (Length(Result) > 3) and
    (Result[Length(Result)] = '\') do
    Delete(Result, Length(Result), 1);
end;

function GetPreviousRoot(): String;
begin
  Result := ExpandConstant('{reg:HKLM\Software\MineRailMonitor,InstallLocation|}');
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  PreviousRoot: String;
begin
  Result := True;
  if CurPageID <> wpSelectDir then
    exit;

  PreviousRoot := GetPreviousRoot();
  if (PreviousRoot <> '') and
     (CompareText(NormalizeRoot(ExpandConstant('{app}')),
       NormalizeRoot(PreviousRoot)) <> 0) then begin
    MsgBox(
      '已安装版本位于 ' + PreviousRoot + '。当前版本不支持升级时迁移安装目录。' +
      '请继续使用原安装目录；如需迁移，请先完成独立的数据迁移流程。',
      mbCriticalError,
      MB_OK);
    Result := False;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  PreviousRoot: String;
begin
  Result := '';
  PreviousRoot := GetPreviousRoot();
  if (PreviousRoot <> '') and
     (CompareText(NormalizeRoot(ExpandConstant('{app}')),
       NormalizeRoot(PreviousRoot)) <> 0) then
    Result := '当前版本不支持升级时迁移安装目录，请继续使用原安装目录。';
end;

function IsSilentUninstall(): Boolean;
var
  CommandLine: String;
begin
  CommandLine := Uppercase(GetCmdTail);
  Result := (Pos('/SILENT', CommandLine) > 0) or
    (Pos('/VERYSILENT', CommandLine) > 0);
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  DeleteFieldData := False;
  if IsSilentUninstall then
    exit;

  DeleteFieldData := MsgBox(
    '是否同时删除现场数据和历史记录？',
    mbConfirmation,
    MB_YESNO) = IDYES;
  if DeleteFieldData and
     (MsgBox(
       '将删除 Projects、Data、Backups、Logs 和 Docs 中的现场文件，是否继续？',
       mbConfirmation,
       MB_YESNO) = IDNO) then
    DeleteFieldData := False;
end;

procedure CurUninstallStepChanged(Changes: TUninstallStep);
begin
  if (Changes = usPostUninstall) and DeleteFieldData then begin
    DelTree(ExpandConstant('{app}\Projects'), True, True, True);
    DelTree(ExpandConstant('{app}\Data'), True, True, True);
    DelTree(ExpandConstant('{app}\Backups'), True, True, True);
    DelTree(ExpandConstant('{app}\Logs'), True, True, True);
    DelTree(ExpandConstant('{app}\Docs'), True, True, True);
  end;
end;
