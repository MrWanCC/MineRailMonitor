#define MyAppName "MineRailMonitor"
#define MyAppVersion "1.0.1"

[Setup]
AppId={{8C8B1CB5-4A4B-4B4A-9D48-6A3C93D2F0E1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName=C:\MineRailMonitor
UsePreviousAppDir=yes
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64os
ArchitecturesAllowed=x64os
AllowUNCPath=no
AllowNetworkDrive=no
OutputDir=..\artifacts\installer
OutputBaseFilename=MineRailMonitor-Setup
DisableProgramGroupPage=yes
Uninstallable=yes

[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

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
Root: HKLM; Subkey: "Software\MineRailMonitor"; ValueType: string; ValueName: "InstallLocation"; ValueData: "{app}"; Flags: uninsdeletevalue uninsdeletekeyifempty

[Code]
const
  DotNet48MinimumRelease = 528040;
  SidSystem = '*S-1-5-18';
  SidAdministrators = '*S-1-5-32-544';
  SidUsers = '*S-1-5-32-545';
  UsersReadExecuteRights = '(OI)(CI)(RX)';
  UsersModifyRights = '(OI)(CI)(M)';
  UnsafeRootMessage =
    '请选择一个 MineRailMonitor 专用安装目录，不能使用磁盘根目录、Windows/Program Files 等系统目录，也不能直接使用包含其它文件的共享目录。';
  ReparsePointMessage =
    '安装目录中检测到符号链接或目录联接，为防止权限操作影响目录外数据，安装已停止。';

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
  Result := RemoveBackslashUnlessRoot(DirectoryName);
end;

function IsDriveRoot(const DirectoryName: String): Boolean;
var
  Root: String;
begin
  Root := NormalizeRoot(DirectoryName);
  Result := (ExtractFileDrive(Root) <> '') and
    (CompareText(Root, AddBackslash(ExtractFileDrive(Root))) = 0);
end;

function IsPathEqualOrBelow(const Candidate, Base: String): Boolean;
var
  CandidateValue: String;
  BaseValue: String;
begin
  CandidateValue := NormalizeRoot(Candidate);
  BaseValue := AddBackslash(NormalizeRoot(Base));
  Result :=
    (CompareText(CandidateValue, NormalizeRoot(Base)) = 0) or
    ((Length(CandidateValue) >= Length(BaseValue)) and
      (CompareText(Copy(CandidateValue, 1, Length(BaseValue)), BaseValue) = 0));
end;

function IsRetainedFieldDataDirectory(const DirectoryName: String): Boolean;
begin
  Result :=
    (CompareText(DirectoryName, 'Projects') = 0) or
    (CompareText(DirectoryName, 'Data') = 0) or
    (CompareText(DirectoryName, 'Backups') = 0) or
    (CompareText(DirectoryName, 'Logs') = 0) or
    (CompareText(DirectoryName, 'Docs') = 0);
end;

function IsSafeRetainedFieldDataEntry(const FindData: TFindRec): Boolean;
begin
  Result :=
    IsRetainedFieldDataDirectory(FindData.Name) and
    ((FindData.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0) and
    ((FindData.Attributes and FILE_ATTRIBUTE_REPARSE_POINT) = 0);
end;

function ContainsOnlyRetainedMineRailData(const DirectoryName: String): Boolean;
var
  FindData: TFindRec;
begin
  Result := True;
  if not DirExists(DirectoryName) then
    exit;

  if FindFirst(AddBackslash(DirectoryName) + '*', FindData) then begin
    try
      repeat
        if (FindData.Name <> '.') and (FindData.Name <> '..') and
          not IsSafeRetainedFieldDataEntry(FindData) then begin
          Result := False;
          exit;
        end;
      until not FindNext(FindData);
    finally
      FindClose(FindData);
    end;
  end;
end;

function IsUncOrNetworkPath(const DirectoryName: String): Boolean;
var
  ExpandedPath: String;
begin
  ExpandedPath := ExpandUNCFileName(DirectoryName);
  Result := (Length(ExpandedPath) >= 2) and
    (ExpandedPath[1] = '\') and (ExpandedPath[2] = '\');
end;

function IsReparsePoint(const FindData: TFindRec): Boolean;
begin
  Result := (FindData.Attributes and FILE_ATTRIBUTE_REPARSE_POINT) <> 0;
end;

function TryGetEntryAttributes(const ParentDirectory, EntryName: String;
  var Attributes: Cardinal): Boolean;
var
  FindData: TFindRec;
begin
  Result := False;
  Attributes := 0;
  if not FindFirst(AddBackslash(ParentDirectory) + '*', FindData) then
    exit;

  try
    repeat
      if CompareText(FindData.Name, EntryName) = 0 then begin
        Attributes := FindData.Attributes;
        Result := True;
        exit;
      end;
    until not FindNext(FindData);
  finally
    FindClose(FindData);
  end;
end;

function TryGetPathAttributes(const Path: String;
  var Attributes: Cardinal): Boolean;
var
  ParentDirectory: String;
  EntryName: String;
begin
  Result := False;
  Attributes := 0;
  if (Path = '') or IsDriveRoot(Path) then
    exit;

  ParentDirectory := NormalizeRoot(ExtractFileDir(Path));
  EntryName := ExtractFileName(Path);
  if (ParentDirectory = '') or (EntryName = '') then
    exit;

  Result := TryGetEntryAttributes(ParentDirectory, EntryName, Attributes);
end;

function ValidateRootPathChain(const DirectoryName: String): Boolean;
var
  Current: String;
  ParentDirectory: String;
  Attributes: Cardinal;
begin
  Result := True;
  Current := NormalizeRoot(DirectoryName);

  while (Current <> '') and not IsDriveRoot(Current) and
    (not DirExists(Current)) and (not FileExists(Current)) do begin
    ParentDirectory := NormalizeRoot(ExtractFileDir(Current));
    if ParentDirectory = Current then
      break;
    Current := ParentDirectory;
  end;

  while (Current <> '') and not IsDriveRoot(Current) do begin
    if not TryGetPathAttributes(Current, Attributes) then begin
      if DirExists(Current) or FileExists(Current) then begin
        Result := False;
        exit;
      end;
    end
    else if (Attributes and FILE_ATTRIBUTE_REPARSE_POINT) <> 0 then begin
      Result := False;
      exit;
    end;

    ParentDirectory := NormalizeRoot(ExtractFileDir(Current));
    if ParentDirectory = Current then
      break;
    Current := ParentDirectory;
  end;
end;

function ScanManagedTreeForReparsePoints(const DirectoryName: String): Boolean;
var
  Attributes: Cardinal;
  FindData: TFindRec;
  ChildPath: String;
begin
  Result := True;
  if not TryGetPathAttributes(DirectoryName, Attributes) then begin
    if (not DirExists(DirectoryName)) and (not FileExists(DirectoryName)) then
      exit;
    Result := False;
    exit;
  end;

  if (Attributes and FILE_ATTRIBUTE_REPARSE_POINT) <> 0 then begin
    Result := False;
    exit;
  end;

  if (Attributes and FILE_ATTRIBUTE_DIRECTORY) = 0 then begin
    Result := False;
    exit;
  end;

  if FindFirst(AddBackslash(DirectoryName) + '*', FindData) then begin
    try
      repeat
        if (FindData.Name <> '.') and (FindData.Name <> '..') then begin
          if IsReparsePoint(FindData) then begin
            Result := False;
            exit;
          end;

          if (FindData.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then begin
            ChildPath := AddBackslash(DirectoryName) + FindData.Name;
            if not ScanManagedTreeForReparsePoints(ChildPath) then begin
              Result := False;
              exit;
            end;
          end;
        end;
      until not FindNext(FindData);
    finally
      FindClose(FindData);
    end;
  end;
end;

function ValidateInstallRoot(const SelectedRoot, PreviousRoot: String): String;
var
  Root: String;
begin
  Root := NormalizeRoot(SelectedRoot);
  Result := '';

  if (Root = '') or IsDriveRoot(Root) or IsUncOrNetworkPath(Root) or
    IsPathEqualOrBelow(Root, ExpandConstant('{win}')) or
    IsPathEqualOrBelow(Root, ExpandConstant('{sys}')) or
    IsPathEqualOrBelow(Root, ExpandConstant('{pf}')) or
    IsPathEqualOrBelow(Root, ExpandConstant('{pf32}')) or
    IsPathEqualOrBelow(Root, ExpandConstant('{pf64}')) then begin
    Result := UnsafeRootMessage;
    exit;
  end;

  if not ValidateRootPathChain(Root) then begin
    Result := ReparsePointMessage;
    exit;
  end;

  if not ScanManagedTreeForReparsePoints(AddBackslash(Root) + 'App') then begin
    Result := ReparsePointMessage;
    exit;
  end;
  if not ScanManagedTreeForReparsePoints(AddBackslash(Root) + 'Data') then begin
    Result := ReparsePointMessage;
    exit;
  end;
  if not ScanManagedTreeForReparsePoints(AddBackslash(Root) + 'Backups') then begin
    Result := ReparsePointMessage;
    exit;
  end;
  if not ScanManagedTreeForReparsePoints(AddBackslash(Root) + 'Logs') then begin
    Result := ReparsePointMessage;
    exit;
  end;
  if not ScanManagedTreeForReparsePoints(AddBackslash(Root) + 'Projects') then begin
    Result := ReparsePointMessage;
    exit;
  end;
  if not ScanManagedTreeForReparsePoints(AddBackslash(Root) + 'Docs') then begin
    Result := ReparsePointMessage;
    exit;
  end;

  if FileExists(Root) then begin
    Result := UnsafeRootMessage;
    exit;
  end;

  if PreviousRoot <> '' then
    exit;

  if not DirExists(Root) then
    exit;

  if not ContainsOnlyRetainedMineRailData(Root) then
    Result := UnsafeRootMessage;
end;

function ValidatePreviousRoot(const SelectedRoot, PreviousRoot: String): String;
begin
  Result := '';
  if (PreviousRoot <> '') and
    (CompareText(NormalizeRoot(SelectedRoot), NormalizeRoot(PreviousRoot)) <> 0) then
    Result :=
      '已安装版本位于 ' + PreviousRoot + '。' +
      '当前版本不支持升级时迁移安装目录，请继续使用原安装目录。' +
      '如需迁移，请先完成独立的数据迁移流程。';
end;

function GetPreviousRoot(): String;
begin
  Result := ExpandConstant('{reg:HKLM\Software\MineRailMonitor,InstallLocation|}');
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  PreviousRoot: String;
  ValidationError: String;
begin
  Result := True;
  if CurPageID <> wpSelectDir then
    exit;

  PreviousRoot := GetPreviousRoot();
  ValidationError := ValidateInstallRoot(ExpandConstant('{app}'), PreviousRoot);
  if ValidationError <> '' then begin
    MsgBox(ValidationError, mbCriticalError, MB_OK);
    Result := False;
    exit;
  end;

  ValidationError := ValidatePreviousRoot(ExpandConstant('{app}'), PreviousRoot);
  if ValidationError <> '' then begin
    MsgBox(ValidationError, mbCriticalError, MB_OK);
    Result := False;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  PreviousRoot: String;
begin
  PreviousRoot := GetPreviousRoot();
  Result := ValidateInstallRoot(ExpandConstant('{app}'), PreviousRoot);
  if Result <> '' then
    exit;

  Result := ValidatePreviousRoot(ExpandConstant('{app}'), PreviousRoot);
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
    MB_YESNO or MB_DEFBUTTON2) = IDYES;
  if DeleteFieldData and
     (MsgBox(
       '将删除 Projects、Data、Backups、Logs 和 Docs 中的现场文件，是否继续？',
       mbConfirmation,
       MB_YESNO or MB_DEFBUTTON2) = IDNO) then
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
