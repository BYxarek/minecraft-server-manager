#ifndef AppVersion
  #error AppVersion must be passed by the build script.
#endif
#ifndef PublishDir
  #error PublishDir must be passed by the build script.
#endif
#ifndef JavaDownloadUrl
  #error JavaDownloadUrl must be passed by the build script.
#endif
#ifndef JavaSha256
  #error JavaSha256 must be passed by the build script.
#endif

[Setup]
AppId={{A26D1C58-4D4A-48EB-8CB9-7D59963F6A86}
AppName=Minecraft Server Manager
AppVersion={#AppVersion}
AppVerName=Minecraft Server Manager {#AppVersion}
AppPublisher=BYxarek
AppPublisherURL=https://github.com/BYxarek/minecraft-server-manager
AppSupportURL=https://github.com/BYxarek/minecraft-server-manager/issues
AppUpdatesURL=https://github.com/BYxarek/minecraft-server-manager/releases
DefaultDirName={localappdata}\Programs\Minecraft Server Manager
DefaultGroupName=Minecraft Server Manager
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
MinVersion=10.0
WizardStyle=modern
LicenseFile=..\LICENSE
SetupIconFile=..\src\MinecraftServerManager\Resources\app.ico
Compression=lzma2
SolidCompression=yes
SetupLogging=yes
CloseApplications=yes
CloseApplicationsFilter=MinecraftServerManager.exe
RestartApplications=no
UninstallDisplayIcon={app}\MinecraftServerManager.exe
OutputDir=..\dist
OutputBaseFilename=MinecraftServerManager-v{#AppVersion}-win-x64-setup

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "JavaSetup.ps1"; Flags: dontcopy noencryption
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{autoprograms}\Minecraft Server Manager"; Filename: "{app}\MinecraftServerManager.exe"
Name: "{autodesktop}\Minecraft Server Manager"; Filename: "{app}\MinecraftServerManager.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\MinecraftServerManager.exe"; Description: "{cm:LaunchProgram,Minecraft Server Manager}"; Flags: nowait postinstall skipifsilent

[CustomMessages]
english.JavaPageTitle=Optional Java 25
russian.JavaPageTitle=Java 25 по выбору
english.JavaPageDescription=Install Java 25 if you want to create Minecraft Java servers.
russian.JavaPageDescription=Установите Java 25, если хотите создавать серверы Minecraft Java.
english.JavaNotFound=Java is not currently detected on this computer.
russian.JavaNotFound=Java сейчас не найдена на этом компьютере.
english.JavaFound=Installed Java: %s (%s)
russian.JavaFound=Установлена Java: %s (%s)
english.JavaInstallOption=Download and install Eclipse Temurin Java 25 for this user
russian.JavaInstallOption=Скачать и установить Eclipse Temurin Java 25 для текущего пользователя
english.JavaInstallNote=Java 25 will be added to your user PATH and used automatically for new Java servers. Java remains installed if you remove this application.
russian.JavaInstallNote=Java 25 будет добавлена в PATH пользователя и автоматически выбрана для новых Java-серверов. При удалении приложения Java сохранится.
english.JavaDownloadFailed=Java 25 could not be downloaded. Check your connection and try again.
russian.JavaDownloadFailed=Не удалось скачать Java 25. Проверьте подключение и повторите попытку.
english.JavaInstallFailed=Java 25 installation failed. The application was not installed. See the setup log for details.
russian.JavaInstallFailed=Не удалось установить Java 25. Приложение не установлено. Подробности в журнале установщика.

[Code]
var
  JavaPage: TWizardPage;
  JavaStatus: TNewStaticText;
  JavaOption: TNewCheckBox;
  DownloadPage: TDownloadWizardPage;

function PowerShellParams(const Mode: String): String;
begin
  Result := '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{tmp}\JavaSetup.ps1') + '" -Mode ' + Mode +
    ' -ManagedRoot "' + ExpandConstant('{localappdata}\MinecraftServerManager\Java25') + '"';
end;

procedure InitializeWizard;
var
  StatusFile, JavaVersion, JavaPath, Params: String;
  ExitCode: Integer;
begin
  ExtractTemporaryFile('JavaSetup.ps1');
  JavaPage := CreateCustomPage(wpSelectDir, CustomMessage('JavaPageTitle'), CustomMessage('JavaPageDescription'));
  JavaStatus := TNewStaticText.Create(JavaPage);
  JavaStatus.Parent := JavaPage.Surface;
  JavaStatus.Left := 0;
  JavaStatus.Top := 8;
  JavaStatus.Width := JavaPage.SurfaceWidth;
  JavaStatus.Height := 48;
  JavaStatus.AutoSize := False;
  JavaStatus.WordWrap := True;
  JavaOption := TNewCheckBox.Create(JavaPage);
  JavaOption.Parent := JavaPage.Surface;
  JavaOption.Left := 0;
  JavaOption.Top := 70;
  JavaOption.Width := JavaPage.SurfaceWidth;
  JavaOption.Caption := CustomMessage('JavaInstallOption');
  JavaOption.Checked := False;
  with TNewStaticText.Create(JavaPage) do begin
    Parent := JavaPage.Surface;
    Left := 0;
    Top := 108;
    Width := JavaPage.SurfaceWidth;
    Height := 90;
    AutoSize := False;
    WordWrap := True;
    Caption := CustomMessage('JavaInstallNote');
  end;
  StatusFile := ExpandConstant('{tmp}\java-status.ini');
  Params := PowerShellParams('Detect') + ' -StatusFile "' + StatusFile + '"';
  if Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Params, '', SW_HIDE, ewWaitUntilTerminated, ExitCode) and (ExitCode = 0) then begin
    JavaVersion := GetIniString('Java', 'Version', '', StatusFile);
    JavaPath := GetIniString('Java', 'Path', '', StatusFile);
    if JavaVersion <> '' then JavaStatus.Caption := Format(CustomMessage('JavaFound'), [JavaVersion, JavaPath])
    else JavaStatus.Caption := CustomMessage('JavaNotFound');
  end else JavaStatus.Caption := CustomMessage('JavaNotFound');
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), nil);
  DownloadPage.ShowBaseNameInsteadOfUrl := True;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID <> wpReady) or (not JavaOption.Checked) then Exit;
  DownloadPage.Clear;
  DownloadPage.Add('{#JavaDownloadUrl}', 'java25.zip', '{#JavaSha256}');
  DownloadPage.Show;
  try
    try DownloadPage.Download;
    except
      Log(GetExceptionMessage);
      SuppressibleMsgBox(CustomMessage('JavaDownloadFailed') + #13#10 + GetExceptionMessage, mbCriticalError, MB_OK, IDOK);
      Result := False;
    end;
  finally DownloadPage.Hide; end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Params: String;
  ExitCode: Integer;
begin
  Result := '';
  if not JavaOption.Checked then Exit;
  Params := PowerShellParams('Install') +
    ' -ArchivePath "' + ExpandConstant('{tmp}\java25.zip') + '"' +
    ' -SettingsPath "' + ExpandConstant('{userappdata}\MinecraftServerManager\java.json') + '"' +
    ' -ExpectedSha256 "{#JavaSha256}"';
  if (not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Params, '', SW_HIDE, ewWaitUntilTerminated, ExitCode)) or (ExitCode <> 0) then begin
    Log('JavaSetup.ps1 failed with exit code ' + IntToStr(ExitCode));
    Result := CustomMessage('JavaInstallFailed');
  end;
end;
