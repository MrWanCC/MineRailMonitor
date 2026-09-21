#define MyAppName "MineRailMonitor"
#define MyAppVersion "1.0.0"

[Setup]
AppId={{8C8B1CB5-4A4B-4B4A-9D48-6A3C93D2F0E1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName=C:\MineRailMonitor
UsePreviousAppDir=yes
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64
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
