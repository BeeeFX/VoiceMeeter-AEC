#ifndef MyAppVersion
  #define MyAppVersion "1.1.1"
#endif
#ifndef BuildRoot
  #define BuildRoot "..\target\release\app"
#endif
#ifndef OutputRoot
  #define OutputRoot "..\dist"
#endif

[Setup]
AppId={{A3A19D55-38A5-4E5D-AED0-7C84233E9F94}
AppName=VoiceMeeter AEC
AppVersion={#MyAppVersion}
AppPublisher=BeeeFX
AppPublisherURL=https://github.com/BeeeFX/VoiceMeeter-AEC
AppSupportURL=https://github.com/BeeeFX/VoiceMeeter-AEC/issues
AppUpdatesURL=https://github.com/BeeeFX/VoiceMeeter-AEC/releases/latest
DefaultDirName={localappdata}\Programs\VoiceMeeter AEC
DefaultGroupName=VoiceMeeter AEC
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
SetupArchitecture=x64
ArchitecturesAllowed=x64compatible
OutputDir={#OutputRoot}
OutputBaseFilename=VoiceMeeter-AEC-Setup
SetupIconFile=..\app\Assets\app.ico
UninstallDisplayIcon={app}\VoiceMeeter AEC.exe
UninstallDisplayName=VoiceMeeter AEC
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
AppMutex=Local\VoiceMeeterAECDesktopApp,Local\VoiceMeeterAECInsert
InfoBeforeFile=BeforeInstall.txt
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany=BeeeFX
VersionInfoDescription=VoiceMeeter AEC installer
VersionInfoProductName=VoiceMeeter AEC
VersionInfoProductVersion={#MyAppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#BuildRoot}\VoiceMeeter AEC.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildRoot}\voicemeeter-aec.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}\licenses"; DestName: "GPL-3.0.txt"; Flags: ignoreversion
Source: "..\THIRD-PARTY-EN.md"; DestDir: "{app}\licenses"; Flags: ignoreversion
Source: "..\THIRD-PARTY.md"; DestDir: "{app}\licenses"; Flags: ignoreversion
Source: "..\vendor\DOTNET-LICENSE.txt"; DestDir: "{app}\licenses"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\VoiceMeeter AEC"; Filename: "{app}\VoiceMeeter AEC.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\VoiceMeeter AEC"; Filename: "{app}\VoiceMeeter AEC.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\VoiceMeeter AEC.exe"; Description: "Open VoiceMeeter AEC"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  StartupCommand: String;
begin
  if (CurUninstallStep = usUninstall) and
     RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run',
       'VoiceMeeter AEC', StartupCommand) and
     (Pos(Lowercase(AddBackslash(ExpandConstant('{app}'))), Lowercase(StartupCommand)) > 0) then
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run',
      'VoiceMeeter AEC');
end;
