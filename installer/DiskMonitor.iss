#define AppName "Disk Monitor"
#ifndef AppVersion
#define AppVersion "0.0.0.0"
#endif
#ifndef SourceDir
#define SourceDir "..\src\DiskMonitor\obj\script-publish"
#endif
#ifndef OutputDir
#define OutputDir "..\artifacts"
#endif

[Setup]
AppId={{7375E2FD-A715-449C-949F-2E544F46B05C}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppName}
AppPublisherURL=https://github.com/rockenrooster/DiskMonitor
DefaultDirName={autopf}\DiskMonitor
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=DiskMonitor-Setup
SetupIconFile=..\src\DiskMonitor\Assets\DiskMonitor.ico
UninstallDisplayIcon={app}\DiskMonitor.exe
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
CloseApplications=force
CloseApplicationsFilter=DiskMonitor.exe
RestartApplications=no
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppName}
VersionInfoDescription={#AppName} Setup
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}

[Tasks]
Name: "startup"; Description: "Start Disk Monitor when I sign in"; GroupDescription: "Options:"
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Options:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\DiskMonitor.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\DiskMonitor.exe"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\DiskMonitor.exe"; Tasks: desktopicon

[Registry]
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\DiskMonitor.exe"; ValueType: string; ValueName: ""; ValueData: "{app}\DiskMonitor.exe"; Flags: uninsdeletekey
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\DiskMonitor.exe"; ValueType: string; ValueName: "Path"; ValueData: "{app}"; Flags: uninsdeletekey

[Run]
Filename: "{app}\DiskMonitor.exe"; Parameters: "--register-startup"; Tasks: startup; Flags: runasoriginaluser runhidden waituntilterminated
Filename: "{app}\DiskMonitor.exe"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent runasoriginaluser
Filename: "{app}\DiskMonitor.exe"; Parameters: "--tray"; Flags: nowait skipifnotsilent runasoriginaluser

[UninstallRun]
Filename: "{app}\DiskMonitor.exe"; Parameters: "--exit"; Flags: runhidden; RunOnceId: "StopApp"
Filename: "{app}\DiskMonitor.exe"; Parameters: "--unregister-startup"; Flags: runhidden; RunOnceId: "RemoveStartup"
Filename: "{app}\DiskMonitor.exe"; Parameters: "--elevated-op unregister-all-tasks"; Flags: runhidden; RunOnceId: "RemoveTasks"

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\DiskMonitor\Updates"
