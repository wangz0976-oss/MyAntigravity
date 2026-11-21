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
DefaultDirName={localappdata}\MyAntigravity
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
zh_cn.Shortcuts=快捷方式：
zh_cn.DesktopShortcut=创建桌面快捷方式
zh_cn.StartMenuShortcut=创建开始菜单快捷方式
zh_cn.CliTools=命令行工具：
zh_cn.CliTask=安装 Google 官方 Antigravity CLI（需要联网，已有可用版本时跳过）
zh_cn.CliProgress=正在检查或安装 Google 官方 Antigravity CLI，请稍候…
zh_cn.CliReady=Google 官方 Antigravity CLI 已就绪。
zh_cn.CliFailedStatus=官方 CLI 未就绪。MyAntigravity 已安装。
zh_cn.CliFailed=MyAntigravity 已安装，官方 CLI 暂未就绪。打开软件后可从“账户 → 检测 CLI 版本”重试。
zh_cn.CliExit=CLI 安装检查退出码：%1。
zh_cn.OpenApplication=打开 MyAntigravity
zh_cn.PreserveFailed=无法保留当前程序以便恢复。请关闭 MyAntigravity 后重试。
zh_cn.RestoreFailed=旧版程序保留在 %1。请关闭 MyAntigravity 后重新运行安装程序以恢复。

[Files]
Source: "{#PayloadFolder}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Dirs]
Name: "{app}\data"; Flags: uninsneveruninstall

[Icons]
Name: "{userdesktop}\MyAntigravity"; Filename: "{app}\MyAntigravity.exe"; WorkingDir: "{app}"; Tasks: desktopicon
Name: "{autoprograms}\MyAntigravity"; Filename: "{app}\MyAntigravity.exe"; WorkingDir: "{app}"; Tasks: startmenu

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"; GroupDescription: "{cm:Shortcuts}"; Flags: unchecked
Name: "startmenu"; Description: "{cm:StartMenuShortcut}"; GroupDescription: "{cm:Shortcuts}"; Flags: unchecked
Name: "installcli"; Description: "{cm:CliTask}"; GroupDescription: "{cm:CliTools}"; Flags: unchecked checkedonce

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

procedure InstallOptionalCli;
var
  ReportPath, Detail, LineEnding: String;
  Report: TArrayOfString;
  ResultCode, Index: Integer;
  CancelWasEnabled: Boolean;
begin
  ReportPath := ExpandConstant('{tmp}\myantigravity-cli-error.txt');
  LineEnding := #13#10;
  WizardForm.StatusLabel.Caption := CustomMessage('CliProgress');
  WizardForm.FilenameLabel.Caption := '';
  WizardForm.ProgressGauge.Style := npbstMarquee;
  CancelWasEnabled := WizardForm.CancelButton.Enabled;
  WizardForm.CancelButton.Enabled := False;
  Detail := '';
  try
    if not Exec(InstalledExe, '--setup-cli "' + ReportPath + '"', ExpandConstant('{app}'),
      SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      Detail := SysErrorMessage(ResultCode)
    else if ResultCode <> 0 then
    begin
      Detail := FmtMessage(CustomMessage('CliExit'), [IntToStr(ResultCode)]);
      if LoadStringsFromFile(ReportPath, Report) then
        for Index := 0 to GetArrayLength(Report) - 1 do
          Detail := Detail + LineEnding + Report[Index];
    end;
  finally
    WizardForm.ProgressGauge.Style := npbstNormal;
    WizardForm.ProgressGauge.Position := WizardForm.ProgressGauge.Max;
    WizardForm.CancelButton.Enabled := CancelWasEnabled;
  end;
  if Detail = '' then
    WizardForm.StatusLabel.Caption := CustomMessage('CliReady')
  else
  begin
    WizardForm.StatusLabel.Caption := CustomMessage('CliFailedStatus');
    Log('Optional Antigravity CLI: ' + Detail);
    SuppressibleMsgBox(CustomMessage('CliFailed') + LineEnding + LineEnding + Detail,
      mbError, MB_OK, IDOK);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and not WizardSilent and WizardIsTaskSelected('installcli') then
    InstallOptionalCli;
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
