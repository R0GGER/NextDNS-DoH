#ifndef MyAppVersion
#define MyAppVersion "1.0.2"
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
DefaultDirName={localappdata}\Programs\NextDNS DoH
DefaultGroupName={#MyAppName}
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
PrivilegesRequired=lowest
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
Filename: "{app}\{#MyAppExeName}"; Flags: nowait

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
