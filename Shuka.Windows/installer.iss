#define MyAppName "Shuka"
#ifndef MyAppVersion
  #define MyAppVersion "1.0.7"
#endif
#define MyAppPublisher "Shuka"
#define MyAppExeName "Shuka.exe"

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Shuka
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\installer_output
OutputBaseFilename=Shuka_Setup
Compression=lzma
SolidCompression=yes
PrivilegesRequired=lowest
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
; All root-level publish files (includes .NET runtime for self-contained app)
Source: "bin\publish\*";                                  DestDir: "{app}"; Flags: ignoreversion
Source: "ShukaIcon.ico";                                  DestDir: "{app}"; Flags: ignoreversion
Source: "bin\publish\.playwright\*";                      DestDir: "{app}\.playwright"; Flags: ignoreversion recursesubdirs
Source: "bin\publish\runtimes\*";                         DestDir: "{app}\runtimes"; Flags: ignoreversion recursesubdirs skipifsourcedoesntexist
Source: "download-epub.bat";                              DestDir: "{app}"; Flags: ignoreversion

[Icons]
; Launch via PowerShell so a proper console window opens and stays visible
Name: "{userprograms}\{#MyAppName}"; Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoExit -NoProfile -ExecutionPolicy Bypass -Command ""Set-Location '{app}'; & '{app}\{#MyAppExeName}'; if ($LASTEXITCODE -ne 0) {{ Read-Host 'Press Enter to close' }}"""; WorkingDir: "{app}"; IconFilename: "{app}\ShukaIcon.ico"
Name: "{userdesktop}\{#MyAppName}";  Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoExit -NoProfile -ExecutionPolicy Bypass -Command ""Set-Location '{app}'; & '{app}\{#MyAppExeName}'; if ($LASTEXITCODE -ne 0) {{ Read-Host 'Press Enter to close' }}"""; WorkingDir: "{app}"; IconFilename: "{app}\ShukaIcon.ico"; Tasks: desktopicon

[Run]
; Install Playwright Chromium browser (needed for Cloudflare-protected sites)
Filename: "{app}\Shuka.exe"; Parameters: "playwright install chromium"; Description: "Installing browser for Cloudflare bypass..."; StatusMsg: "Please wait, installing browser components (Cloudflare bypass)..."; Flags: waituntilterminated
; Launch via PowerShell after install
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoExit -NoProfile -ExecutionPolicy Bypass -Command ""Set-Location '{app}'; & '{app}\{#MyAppExeName}'"""; WorkingDir: "{app}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
