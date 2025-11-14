#define MyAppName "MenuBu Yazıcı Ajanı"
#define MyAppVersion "2.0.0"
#define MyAppPublisher "MenuBu"
#define MyAppExeName "MenuBuPrinterAgent.exe"
#define PublishDir "..\publish\win-x64"
#define WebView2InstallerFile "MicrosoftEdgeWebView2RuntimeInstallerX64.exe"
#define WebView2InstallerSource "Dependencies\" + WebView2InstallerFile

[Setup]
AppId={{1F18A0E2-83D5-4E66-9E0D-7E4E2F9383F6}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={pf}\MenuBu\PrinterAgent
DefaultGroupName=MenuBu
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=MenuBuPrinterAgentSetup
Compression=lzma
SolidCompression=yes
SetupIconFile=..\icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#WebView2InstallerSource}"; DestDir: "{tmp}"; DestName: "{#WebView2InstallerFile}"; Flags: deleteafterinstall

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "MenuBuPrinterAgent"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue

[Run]
Filename: "{tmp}\{#WebView2InstallerFile}"; Parameters: "/silent /install"; StatusMsg: "Microsoft Edge WebView2 Runtime kuruluyor..."; Flags: waituntilterminated
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}\logs"
