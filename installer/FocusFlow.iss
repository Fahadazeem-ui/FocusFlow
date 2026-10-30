#define AppName "FocusFlow"
#define AppVersion "0.3.0"

[Setup]
AppId={{B7A1D9F2-4C3E-4E8A-9F11-5D2C7A6E1F40}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=FocusFlow
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
OutputDir=Output
OutputBaseFilename=FocusFlow-Setup
Compression=lzma2/max
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\FocusFlow.exe
WizardSizePercent=100
DisableWelcomePage=no

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked
Name: "startup"; Description: "Start FocusFlow with Windows"; Flags: unchecked

[Files]
Source: "..\publish\FocusFlow.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\FocusFlow.exe"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\FocusFlow.exe"; Tasks: desktopicon
Name: "{userstartup}\{#AppName}"; Filename: "{app}\FocusFlow.exe"; Tasks: startup

[Run]
Filename: "{app}\FocusFlow.exe"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM FocusFlow.exe"; Flags: runhidden; RunOnceId: "KillApp"
