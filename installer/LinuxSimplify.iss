; Inno Setup script for LinuxSimplify.
; Build with: installer\build-installer.bat  (or run ISCC.exe on this file after publishing)

#define MyAppName "LinuxSimplify"
#define MyAppVersion "1.0.1"
#define MyAppPublisher "panmauk"
#define MyAppURL "https://github.com/panmauk/LinuxSimplify"
#define MyAppExeName "LinuxSimplify.exe"
#define PublishDir "..\bin\Release\net8.0-windows\win-x64\publish"

[Setup]
AppId={{5D172EDD-4355-405A-B531-7C35CEB22631}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=Output
OutputBaseFilename=LinuxSimplify-Setup-{#MyAppVersion}
SetupIconFile=..\app.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Installs for all users under Program Files, which needs admin. (The app itself runs as a normal
; user and asks for admin rights only when it writes the USB drive.)
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
