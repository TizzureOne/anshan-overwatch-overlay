#ifndef AppVersion
  #error AppVersion must be supplied by publish.ps1
#endif
#ifndef PublishDir
  #error PublishDir must be supplied by publish.ps1
#endif

[Setup]
AppId={{B67B9F75-6EC5-4BBA-87B8-A9F722DF7281}
AppName=安山守望
AppVersion={#AppVersion}
AppVerName=安山守望 {#AppVersion}
AppPublisher=TizzureOne
AppPublisherURL=https://github.com/TizzureOne/anshan-overwatch-overlay
AppSupportURL=https://github.com/TizzureOne/anshan-overwatch-overlay/issues
DefaultDirName={localappdata}\Programs\AnshanOverlay
DefaultGroupName=安山守望
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\..\dist
OutputBaseFilename=AnshanOverlay-{#AppVersion}-Setup-x64
SetupIconFile=..\..\src\AnshanOverlay\Assets\app.ico
UninstallDisplayIcon={app}\AnshanOverlay.exe
AppMutex=Local\AnshanOverlay.Desktop
CloseApplications=no
RestartApplications=no
WizardStyle=modern
Compression=lzma2/normal
SolidCompression=yes
SetupLogging=yes

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\安山守望"; Filename: "{app}\AnshanOverlay.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\安山守望"; Filename: "{app}\AnshanOverlay.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\AnshanOverlay.exe"; Description: "{cm:LaunchProgram,安山守望}"; Flags: nowait postinstall skipifsilent
