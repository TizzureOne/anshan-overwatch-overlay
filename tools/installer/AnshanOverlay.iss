#ifndef AppVersion
  #error AppVersion must be supplied by publish.ps1
#endif
#ifndef PublishDir
  #error PublishDir must be supplied by publish.ps1
#endif
#ifdef Lite
  #define VariantName " (Lite)"
  #define FileSuffix "-Lite"
#else
  #define VariantName ""
  #define FileSuffix ""
#endif

[Setup]
AppId={{B67B9F75-6EC5-4BBA-87B8-A9F722DF7281}
AppName=安山守望
AppVersion={#AppVersion}
AppVerName=安山守望 {#AppVersion}{#VariantName}
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
OutputBaseFilename=AnshanOverlay-{#AppVersion}{#FileSuffix}-Setup-x64
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

#ifdef Lite
[CustomMessages]
chinesesimplified.MissingRuntimes=精简版需要先安装以下运行组件：%n%n%1%n请下载并安装对应的 .NET 8 x64 组件，再继续安装。也可使用同一版本的完整版。%n%n微软官方下载：%nhttps://dotnet.microsoft.com/zh-cn/download/dotnet/8.0
chinesesimplified.DownloadRuntimes=下载运行组件
english.MissingRuntimes=The Lite edition requires these runtime components:%n%n%1%nInstall the .NET 8 x64 components, then retry. You can also use the Full installer for this release.%n%nOfficial Microsoft downloads:%nhttps://dotnet.microsoft.com/en-us/download/dotnet/8.0
english.DownloadRuntimes=Download runtimes

[Code]
var
  DownloadButton: TNewButton;
  RuntimesMissing: Boolean;

function IsX64Host(const Filename: String): Boolean;
var
  Data: AnsiString;
  Offset: Integer;
begin
  Result := False;
  if not LoadStringFromFile(Filename, Data) then Exit;
  if Length(Data) < 64 then Exit;
  Offset := Ord(Data[61]) + Ord(Data[62]) * 256 + Ord(Data[63]) * 65536 + Ord(Data[64]) * 16777216;
  if (Offset < 0) or (Offset > Length(Data) - 6) then Exit;
  Result := (Copy(Data, Offset + 1, 4) = 'PE' + #0 + #0) and
    (Ord(Data[Offset + 5]) = $64) and (Ord(Data[Offset + 6]) = $86);
end;

function DotNetRoot: String;
begin
  Result := GetEnv('DOTNET_ROOT_X64');
  if Result = '' then Result := GetEnv('DOTNET_ROOT');
  if Result = '' then
    RegQueryStringValue(HKLM32, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64', 'InstallLocation', Result);
  if Result = '' then begin
    Result := ExpandConstant('{commonpf64}\dotnet');
    if IsArm64 then Result := Result + '\x64';
  end;
end;

function HasRuntime(const Lines: TArrayOfString; const Framework: String): Boolean;
var
  I: Integer;
  Prefix, Patch: String;
begin
  Result := False;
  Prefix := Framework + ' 8.0.';
  for I := 0 to GetArrayLength(Lines) - 1 do begin
    if Pos(Prefix, Lines[I]) = 1 then begin
      Patch := Copy(Lines[I], Length(Prefix) + 1, Pos(' [', Lines[I]) - Length(Prefix) - 1);
      if StrToIntDef(Patch, -1) >= 0 then begin
        Result := True;
        Exit;
      end;
    end;
  end;
end;

procedure DownloadRuntimes(Sender: TObject);
var
  ResultCode: Integer;
  Url: String;
begin
  Url := 'https://dotnet.microsoft.com/en-us/download/dotnet/8.0';
  if ActiveLanguage = 'chinesesimplified' then
    Url := 'https://dotnet.microsoft.com/zh-cn/download/dotnet/8.0';
  ShellExec('open', Url, '', '', SW_SHOWNORMAL, ewNoWait, ResultCode);
end;

procedure InitializeWizard;
begin
  DownloadButton := TNewButton.Create(WizardForm);
  DownloadButton.Parent := WizardForm;
  DownloadButton.SetBounds(ScaleX(10), WizardForm.BackButton.Top, ScaleX(140), WizardForm.BackButton.Height);
  DownloadButton.Caption := CustomMessage('DownloadRuntimes');
  DownloadButton.OnClick := @DownloadRuntimes;
  DownloadButton.Visible := False;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  DownloadButton.Visible := (CurPageID = wpPreparing) and RuntimesMissing;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Output: TExecOutput;
  Lines: TArrayOfString;
  Host, Missing: String;
  ResultCode: Integer;
begin
  Host := AddBackslash(DotNetRoot) + 'dotnet.exe';
  if IsX64Host(Host) then
    if ExecAndCaptureOutput(Host, '--list-runtimes', '', SW_HIDE, ewWaitUntilTerminated, ResultCode, Output) then
      if (ResultCode = 0) and not Output.Error then Lines := Output.StdOut;
  Missing := '';
  if not HasRuntime(Lines, 'Microsoft.NETCore.App') or not HasRuntime(Lines, 'Microsoft.WindowsDesktop.App') then
    Missing := '  .NET Desktop Runtime 8 (x64)' + #13#10;
  if not HasRuntime(Lines, 'Microsoft.AspNetCore.App') then
    Missing := Missing + '  ASP.NET Core Runtime 8 (x64)' + #13#10;
  RuntimesMissing := Missing <> '';
  if RuntimesMissing then begin
    Result := FmtMessage(CustomMessage('MissingRuntimes'), [Missing]);
    Log(Result);
  end else begin
    Result := '';
    Log('Required .NET 8 x64 runtimes detected.');
  end;
  DownloadButton.Visible := RuntimesMissing;
end;
#endif
