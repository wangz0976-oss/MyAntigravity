; MyAntigravity Windows x64 installer.
#ifndef ReleaseVersion
  #error ReleaseVersion is required
#endif

[Setup]
AppId={{F2652C6E-3D30-4731-97C3-487EB25DC0E0}
AppName=MyAntigravity
AppVersion={#ReleaseVersion}
AppPublisher=wangz0976-oss
AppPublisherURL=https://github.com/wangz0976-oss/MyAntigravity
DefaultDirName={src}\installed
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir={#OutputFolder}
OutputBaseFilename=MyAntigravity-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
CloseApplicationsFilter=MyAntigravity.exe
RestartApplications=no
UninstallDisplayIcon={app}\MyAntigravity.exe
UninstallFilesDir={app}
UsePreviousAppDir=yes
ShowLanguageDialog=no

[Languages]
Name: "zh_cn"; MessagesFile: "ChineseSimplified.isl"

[CustomMessages]
zh_cn.StartMenuShortcut=创建开始菜单快捷方式
zh_cn.OpenApplication=打开 MyAntigravity
zh_cn.PreserveFailed=无法保留当前程序以便恢复。请关闭 MyAntigravity 后重试。
zh_cn.RestoreFailed=旧版程序保留在 %1。请关闭 MyAntigravity 后重新运行安装程序以恢复。

[Files]
Source: "{#PayloadFolder}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\MyAntigravity"; Filename: "{app}\MyAntigravity.exe"; WorkingDir: "{app}"; Tasks: startmenu

[Tasks]
Name: "startmenu"; Description: "{cm:StartMenuShortcut}"; Flags: unchecked

[Run]
Filename: "{app}\MyAntigravity.exe"; Description: "{cm:OpenApplication}"; Flags: nowait postinstall skipifsilent unchecked

[Code]
var
  InstalledExe, RollbackExe: String;
  UpgradeCommitted: Boolean;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  PreviousExe: String;
begin
  Result := '';
  InstalledExe := ExpandConstant('{app}\MyAntigravity.exe');
  PreviousExe := ExpandConstant('{app}\.myantigravity-rollback.exe');
  if FileExists(PreviousExe) then
    RollbackExe := PreviousExe
  else if FileExists(InstalledExe) then
  begin
    if CopyFile(InstalledExe, PreviousExe, True) then
      RollbackExe := PreviousExe
    else
    begin
      DeleteFile(PreviousExe);
      Result := CustomMessage('PreserveFailed');
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssDone then
  begin
    UpgradeCommitted := True;
    if RollbackExe <> '' then
      DeleteFile(RollbackExe);
  end;
end;

procedure DeinitializeSetup;
begin
  if UpgradeCommitted or (RollbackExe = '') then
    exit;
  if (FileExists(InstalledExe) and
      (GetSHA256OfFile(InstalledExe) = GetSHA256OfFile(RollbackExe))) or
      CopyFile(RollbackExe, InstalledExe, False) then
    DeleteFile(RollbackExe)
  else
  begin
    Log('The previous application remains at ' + RollbackExe);
    SuppressibleMsgBox(FmtMessage(CustomMessage('RestoreFailed'), [RollbackExe]),
      mbError, MB_OK, IDOK);
  end;
end;
