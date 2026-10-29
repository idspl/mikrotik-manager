#ifndef AppVersion
  #define AppVersion "0.7.0"
#endif
[Setup]
AppId={{B453865F-731A-4E09-A7AC-E661DE931432}
AppName=MikroTik Manager
AppVersion={#AppVersion}
AppPublisher=Indigo Data Services Pvt Ltd
AppPublisherURL=https://github.com/idspl/mikrotik-manager
DefaultDirName={autopf}\Indigo\MikroTik Manager
DefaultGroupName=MikroTik Manager
OutputDir=..\release\installer
OutputBaseFilename=MikroTikManager-Setup
SetupIconFile=..\app.ico
UninstallDisplayIcon={app}\MikroTikManager.exe
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=no
RestartApplications=no
AppMutex=Global\Indigo.MikroTikManager.Application
InfoBeforeFile=Install-notes.txt
[Files]
Source: "..\release\win-x64\MikroTikManager.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "Service-Control.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "installed.mode"; DestDir: "{app}"; Flags: ignoreversion
[Icons]
Name: "{group}\MikroTik Manager"; Filename: "{app}\MikroTikManager.exe"
Name: "{commondesktop}\MikroTik Manager"; Filename: "{app}\MikroTikManager.exe"
[Code]
function ServiceAction(Action: String): Boolean;
var ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\Service-Control.ps1') + '" -Action ' + Action,
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if CheckForMutexes('Global\Indigo.MikroTikManager.Application') then begin
    Result := 'Close MikroTik Manager and wait for active background maintenance to finish, then retry.'; exit;
  end;
  if FileExists(ExpandConstant('{app}\Service-Control.ps1')) and not ServiceAction('Stop') then
    Result := 'The scheduler has not stopped. Wait for maintenance to finish, then retry. No files were replaced.';
end;
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    if not ServiceAction('Install') then RaiseException('Scheduler configuration failed. Run Setup again to repair. Existing router data has been retained.');
end;
function InitializeUninstall(): Boolean;
begin
  Result := False;
  if CheckForMutexes('Global\Indigo.MikroTikManager.Application') then begin
    MsgBox('Close MikroTik Manager and wait for background maintenance to finish before uninstalling.', mbError, MB_OK); exit;
  end;
  Result := ServiceAction('Remove');
  if not Result then MsgBox('Could not stop/remove the service. No files have been removed.', mbError, MB_OK);
end;
