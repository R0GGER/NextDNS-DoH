#ifndef MyAppVersion
#define MyAppVersion "1.0.7"
#endif

#define MyAppName "NextDNS DoH"
#define MyAppExeName "nextdns-doh.exe"

[Setup]
AppId={{B4E8C1A7-6F2D-4A91-9E3B-7C5D8F0A2E16}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppName}
AppSupportURL=https://my.nextdns.io
UninstallDisplayName={#MyAppName} {#MyAppVersion}
VersionInfoVersion={#MyAppVersion}
VersionInfoProductVersion={#MyAppVersion}
VersionInfoProductName={#MyAppName}
VersionInfoCompany={#MyAppName}
VersionInfoDescription={#MyAppName} installer
VersionInfoCopyright=Copyright © 2026
VersionInfoOriginalFileName=NextDNS-DoH-{#MyAppVersion}.exe
DefaultDirName={autopf}\NextDNS DoH
DefaultGroupName={#MyAppName}
UsePreviousAppDir=no
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableWelcomePage=yes
DisableReadyPage=yes
DisableFinishedPage=yes
OutputDir=..\dist
OutputBaseFilename=NextDNS-DoH-{#MyAppVersion}
SetupIconFile=..\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
CloseApplications=no
RestartApplications=no

[Files]
Source: "..\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\publish\{#MyAppExeName}.config"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--register-task ""{username}"""; Flags: runhidden waituntilterminated

[UninstallRun]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--unregister-task"; RunOnceId: "UnregisterTask"; Flags: runhidden waituntilterminated

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  if not WizardSilent then
  begin
    { Restart silently after this instance exits, otherwise Inno Setup's mutex
      would immediately abort the second copy. }
    Exec(ExpandConstant('{sys}\cmd.exe'),
      '/c ping 127.0.0.1 -n 2 >nul & start "" "' + ExpandConstant('{srcexe}') + '" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-',
      '', SW_HIDE, ewNoWait, ResultCode);
    Result := False;
    Exit;
  end;

  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM {#MyAppExeName} /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(300);
  Result := True;
end;

{ Versions up to 1.0.5 installed per user into %LocalAppData%. Remove that copy,
  otherwise it keeps its own entry in Settings -> Apps next to the new one. }
procedure RemovePreviousPerUserInstall;
var
  UninstallString: String;
  ResultCode: Integer;
begin
  if not RegQueryStringValue(HKCU,
    'Software\Microsoft\Windows\CurrentVersion\Uninstall\{B4E8C1A7-6F2D-4A91-9E3B-7C5D8F0A2E16}_is1',
    'UninstallString', UninstallString) then
    Exit;

  UninstallString := RemoveQuotes(UninstallString);
  if FileExists(UninstallString) then
    Exec(UninstallString, '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

{ Setup runs elevated. Launching through Explorer keeps the tray app at the normal
  user level, so it uses the scheduled task instead of asking for UAC. }
procedure LaunchApp;
var
  Exe: String;
  ResultCode: Integer;
begin
  Exe := ExpandConstant('{app}\{#MyAppExeName}');
  if not Exec(ExpandConstant('{win}\explorer.exe'), '"' + Exe + '"', ExpandConstant('{app}'), SW_SHOWNORMAL, ewNoWait, ResultCode) then
    Exec(Exe, '', ExpandConstant('{app}'), SW_SHOWNORMAL, ewNoWait, ResultCode);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
    RemovePreviousPerUserInstall
  else if CurStep = ssDone then
    LaunchApp;
end;

function InitializeUninstall(): Boolean;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM {#MyAppExeName} /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(300);
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'NextDNS-DoH');
end;
