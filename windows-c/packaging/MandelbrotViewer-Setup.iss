; Inno Setup script for Mandelbrot Viewer (version-agnostic: the version is
; read from the published exe, so this file never goes stale on version bumps).
; Built via ..\setup.bat (publishes first, then compiles with ISCC).
; Output: ..\dist\MandelF-Setup-X.Y.Z-win-x64.exe (repo scheme: packaging/ + dist/).
#define MyAppName "Mandelbrot Viewer"
#define MyAppVersion4 GetFileVersion("..\published\MandelF.exe")
#if Copy(MyAppVersion4, Len(MyAppVersion4) - 1, 2) == ".0"
  #define MyAppVersion Copy(MyAppVersion4, 1, Len(MyAppVersion4) - 2)
#else
  #define MyAppVersion MyAppVersion4
#endif
#define MyAppPublisher "ciskje"
#define MyAppURL "https://github.com/ciskje/MandelC"
#define MyAppExeName "MandelF.exe"

[Setup]
AppId={{B88299BB-C271-4C1A-90EC-D290CF499055}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppSupportURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
UninstallDisplayName={#MyAppName}
OutputBaseFilename=MandelF-Setup-{#MyAppVersion}-win-x64
SetupIconFile=..\source\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesAllowed=x64compatible x86compatible
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
DisableProgramGroupPage=yes
ShowLanguageDialog=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\published\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion; Excludes: "*.pdb"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Lancia Mandelbrot Viewer"; Flags: nowait postinstall skipifsilent
