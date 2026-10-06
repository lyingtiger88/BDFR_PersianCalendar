#ifndef MySourceDir
  #define MySourceDir "..\\artifacts\\Anahita-win-x64"
#endif
#ifndef MyOutputDir
  #define MyOutputDir "..\\artifacts"
#endif

#define MyAppName "Anahita"
#define MyAppVersion "1.0-test"
#define MyAppPublisher "BDFR"
#define MyAppExeName "BDFR.PersianCalendar.Desktop.exe"

[Setup]
AppId={{5E8E4792-25B8-4C11-A87E-84687ED84E42}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\\Programs\\Anahita
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir={#MyOutputDir}
OutputBaseFilename=Anahita-Setup-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
UninstallDisplayIcon={app}\\{#MyAppExeName}
SetupLogging=yes

[Files]
Source: "{#MySourceDir}\\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\\{#MyAppName}"; Filename: "{app}\\{#MyAppExeName}"
Name: "{autodesktop}\\{#MyAppName}"; Filename: "{app}\\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Run]
Filename: "{app}\\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
