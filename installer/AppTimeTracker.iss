#define MyAppName "time-controller"
#define MyAppVersion "1.11.0"
#define MyAppPublisher "time-controller contributors"
#define MyAppExeName "AppTimeTracker.exe"
#define PublishDir "..\AppTimeTracker\bin\Release\net8.0-windows\win-x64\publish"

[Setup]
AppId={{A77A1E77-2F0E-4F8F-B6E8-7C0F3E6B5A19}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=output
OutputBaseFilename=time-controller_Setup_{#MyAppVersion}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=..\AppTimeTracker\Assets\time-controller.ico

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; User data is stored under %LOCALAPPDATA%\time-controller and is intentionally preserved.
; To remove it manually, delete: %LOCALAPPDATA%\time-controller
