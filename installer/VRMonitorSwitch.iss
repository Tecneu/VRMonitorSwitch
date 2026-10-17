; VR Monitor Switch - Inno Setup 6 script
; Build:  ISCC.exe installer\VRMonitorSwitch.iss /DAppVersion=0.1.0
; Input:  ..\dist\app\*  (output of `dotnet publish`)  and, optionally, redist\parsec-vdd-0.45.0.0.exe
; Output: ..\dist\VRMonitorSwitch-Setup-<version>.exe
;
; The installer needs admin rights for one reason only: installing the Parsec Virtual Display Driver
; (and writing the HKLM resolution presets it reads). The app itself runs as the user.

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
; VersionInfoVersion must be numeric (a.b.c[.d]); AppVersion may be a prerelease like 0.2.0-beta.1 or the CI's
; 0.0.0-ci, so strip everything from the first "-" and append the 4th component.
#define AppVersionBase    Copy(AppVersion, 1, Pos("-", AppVersion + "-") - 1)
#define AppVersionNumeric AppVersionBase + ".0"
#pragma message "AppVersion " + AppVersion + " -> VersionInfoVersion " + AppVersionNumeric
#define AppName        "VR Monitor Switch"
#define AppExeName     "VRMonitorSwitch.exe"
#define AppPublisher   "Jonathan Hernandez"
#define AppRepoUrl     "https://github.com/Tecneu/VRMonitorSwitch"
#define DriverFile     "parsec-vdd-0.45.0.0.exe"
#define DriverUrl      "https://builds.parsec.app/vdd/parsec-vdd-0.45.0.0.exe"
#define DriverSha256   "E23332448FDAF5AA017CB308DB5EF6855FAC526A7DED05D80C039404126D5362"
#define RedistSource   "redist\" + DriverFile

; The driver is embedded when it is present at compile time (scripts\build.ps1 and CI download it).
; Without it the setup still compiles and downloads the driver on the target machine (see [Code]).
#if FileExists(SourcePath + "\" + RedistSource)
  #define DriverBundled "1"
  #pragma message "Parsec VDD redist found: embedding " + RedistSource
#else
  #define DriverBundled "0"
  #pragma message "Parsec VDD redist NOT found: the setup will download it at install time"
#endif

[Setup]
AppId={{B6A6B1F6-3E0C-4A9D-9C5E-6F2B1D7F0A11}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppRepoUrl}
AppSupportURL={#AppRepoUrl}/issues
AppUpdatesURL={#AppRepoUrl}/releases
VersionInfoVersion={#AppVersionNumeric}
VersionInfoProductTextVersion={#AppVersion}
VersionInfoDescription={#AppName} Setup
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
LicenseFile=..\LICENSE
PrivilegesRequired=admin
; x64os (not x64compatible): the Parsec VDD is an x64 kernel-mode driver and cannot load on Windows on ARM,
; even though Arm64 Windows emulates x64 user-mode code. Requires Inno Setup 6.3+.
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.19041
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
OutputDir=..\dist
OutputBaseFilename=VRMonitorSwitch-Setup-{#AppVersion}
SetupIconFile=..\assets\icon.ico
; Ask the user to close a running instance (same mutex name as Program.cs single-instance guard).
AppMutex=Local\VRMonitorSwitch.SingleInstance
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
ShowLanguageDialog=auto

[Languages]
; First entry is the default when the Windows UI language matches none of them.
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
es.CreateDesktopIcon=Crear un acceso directo en el escritorio
en.CreateDesktopIcon=Create a desktop shortcut
es.DriverStatus=Instalando el controlador Parsec Virtual Display Driver...
en.DriverStatus=Installing the Parsec Virtual Display Driver...
es.DriverDownloadTitle=Descargando el controlador de pantalla virtual
en.DriverDownloadTitle=Downloading the virtual display driver
es.DriverDownloadDesc=El instalador necesita descargar Parsec Virtual Display Driver (%1, aprox. 0.5 MB).
en.DriverDownloadDesc=Setup needs to download the Parsec Virtual Display Driver (%1, about 0.5 MB).
es.DriverDownloadFailed=No se pudo descargar el controlador de pantalla virtual:%n%n%1%n%n¿Quieres continuar sin instalarlo? Podrás instalarlo después desde la propia aplicación (botón "Instalar driver").
en.DriverDownloadFailed=The virtual display driver could not be downloaded:%n%n%1%n%nDo you want to continue without it? You can install it later from the application itself ("Install driver" button).
es.DriverHashMismatch=El archivo descargado no coincide con la huella SHA-256 esperada y se ha descartado por seguridad.
en.DriverHashMismatch=The downloaded file does not match the expected SHA-256 fingerprint and was discarded for safety.
es.DriverInstallFailed=La instalación del controlador Parsec Virtual Display Driver terminó con el código %1.%n%nLa aplicación te ofrecerá instalarlo de nuevo al iniciarla.
en.DriverInstallFailed=The Parsec Virtual Display Driver installation finished with code %1.%n%nThe application will offer to install it again on launch.
es.DriverRestartRequired=Es posible que Windows pida reiniciar para activar el controlador de pantalla virtual.
en.DriverRestartRequired=Windows may ask for a restart to activate the virtual display driver.

[Tasks]
; No "unchecked" flag: the desktop shortcut is created by default.
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\dist\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
#if DriverBundled == "1"
Source: "{#RedistSource}"; DestDir: "{app}\redist"; Flags: ignoreversion
#else
; Not embedded: copy it from a redist\ folder next to the setup if the user provides one; otherwise [Code] downloads it.
Source: "{src}\{#RedistSource}"; DestDir: "{app}\redist"; Flags: external skipifsourcedoesntexist ignoreversion
#endif

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\{#AppExeName}"
Name: "{group}\{#AppName} (diagnostics)"; Filename: "{app}\{#AppExeName}"; Parameters: "--diagnose"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
; Extra resolutions the Parsec VDD exposes (max 5). Read by the driver when a display is added.
Root: HKLM; Subkey: "SOFTWARE\Parsec"; Flags: uninsdeletekeyifempty
Root: HKLM; Subkey: "SOFTWARE\Parsec\vdd"; Flags: uninsdeletekeyifempty
Root: HKLM; Subkey: "SOFTWARE\Parsec\vdd\0"; ValueType: dword; ValueName: "width";  ValueData: 5120; Flags: uninsdeletekey
Root: HKLM; Subkey: "SOFTWARE\Parsec\vdd\0"; ValueType: dword; ValueName: "height"; ValueData: 1440; Flags: uninsdeletekey
Root: HKLM; Subkey: "SOFTWARE\Parsec\vdd\0"; ValueType: dword; ValueName: "hz";     ValueData: 60;   Flags: uninsdeletekey
Root: HKLM; Subkey: "SOFTWARE\Parsec\vdd\1"; ValueType: dword; ValueName: "width";  ValueData: 3840; Flags: uninsdeletekey
Root: HKLM; Subkey: "SOFTWARE\Parsec\vdd\1"; ValueType: dword; ValueName: "height"; ValueData: 1080; Flags: uninsdeletekey
Root: HKLM; Subkey: "SOFTWARE\Parsec\vdd\1"; ValueType: dword; ValueName: "hz";     ValueData: 60;   Flags: uninsdeletekey
Root: HKLM; Subkey: "SOFTWARE\Parsec\vdd\2"; ValueType: dword; ValueName: "width";  ValueData: 3840; Flags: uninsdeletekey
Root: HKLM; Subkey: "SOFTWARE\Parsec\vdd\2"; ValueType: dword; ValueName: "height"; ValueData: 1600; Flags: uninsdeletekey
Root: HKLM; Subkey: "SOFTWARE\Parsec\vdd\2"; ValueType: dword; ValueName: "hz";     ValueData: 60;   Flags: uninsdeletekey
Root: HKLM; Subkey: "SOFTWARE\Parsec\vdd\3"; ValueType: dword; ValueName: "width";  ValueData: 2560; Flags: uninsdeletekey
Root: HKLM; Subkey: "SOFTWARE\Parsec\vdd\3"; ValueType: dword; ValueName: "height"; ValueData: 1600; Flags: uninsdeletekey
Root: HKLM; Subkey: "SOFTWARE\Parsec\vdd\3"; ValueType: dword; ValueName: "hz";     ValueData: 60;   Flags: uninsdeletekey
Root: HKLM; Subkey: "SOFTWARE\Parsec\vdd\4"; ValueType: dword; ValueName: "width";  ValueData: 1920; Flags: uninsdeletekey
Root: HKLM; Subkey: "SOFTWARE\Parsec\vdd\4"; ValueType: dword; ValueName: "height"; ValueData: 1200; Flags: uninsdeletekey
Root: HKLM; Subkey: "SOFTWARE\Parsec\vdd\4"; ValueType: dword; ValueName: "hz";     ValueData: 60;   Flags: uninsdeletekey

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Driver installer copy and the app's own files only. The Parsec VDD driver itself is left installed on purpose:
; other applications (Parsec, Sunshine, ...) may be using it. Remove it from "Apps & features" if desired.
Type: filesandordirs; Name: "{app}\redist"

[Code]
const
  DriverFileName  = '{#DriverFile}';
  DriverUrl       = '{#DriverUrl}';
  DriverSha256    = '{#DriverSha256}';
  DriverBundled   = {#DriverBundled};
  // The Parsec VDD setup creates a root-enumerated device of class "Display" (nefcon --create-device-node with
  // DICD_GENERATE_ID), so the devnode lives under Enum\ROOT\DISPLAY\000x and Root\Parsec\VDA is only its
  // HardwareID value, not a key path. Same detection as ParsecVdd.QueryStatus in the app.
  DriverEnumKey      = 'SYSTEM\CurrentControlSet\Enum\ROOT\DISPLAY';
  DriverHardwareId   = 'Root\Parsec\VDA';
  // Older/alternative setups that register the hardware id as the instance path itself.
  DriverLegacyKey    = 'SYSTEM\CurrentControlSet\Enum\Root\Parsec';

var
  DownloadPage: TDownloadWizardPage;
  DownloadedDriverPath: String;   // non-empty when the driver was downloaded to {tmp}
  LastDownloadError: String;      // reason of the last failed download (for the retry prompt)

// True when the REG_MULTI_SZ block (entries separated by #0, as RegQueryMultiStringValue returns it) contains Id.
function MultiStringContains(Multi: String; const Id: String): Boolean;
var
  P: Integer;
  Entry: String;
begin
  Result := False;
  while Multi <> '' do
  begin
    P := Pos(#0, Multi);
    if P = 0 then
    begin
      Entry := Multi;
      Multi := '';
    end
    else
    begin
      Entry := Copy(Multi, 1, P - 1);
      Delete(Multi, 1, P);
    end;
    if CompareText(Entry, Id) = 0 then
    begin
      Result := True;
      exit;
    end;
  end;
end;

function IsDriverInstalled(): Boolean;
var
  Names: TArrayOfString;
  I: Integer;
  SubKey, Ids: String;
begin
  Result := False;
  if RegGetSubkeyNames(HKLM, DriverEnumKey, Names) then
  begin
    for I := 0 to GetArrayLength(Names) - 1 do
    begin
      SubKey := DriverEnumKey + '\' + Names[I];
      if RegQueryMultiStringValue(HKLM, SubKey, 'HardwareID', Ids) then
      begin
        if MultiStringContains(Ids, DriverHardwareId) then
        begin
          Log('Parsec VDD devnode found: HKLM\' + SubKey);
          Result := True;
          exit;
        end;
      end;
    end;
  end;
  if RegKeyExists(HKLM, DriverLegacyKey) or RegKeyExists(HKLM, DriverLegacyKey + '\VDA') then
  begin
    Log('Parsec VDD devnode found (legacy key): HKLM\' + DriverLegacyKey);
    Result := True;
  end;
end;

// Path where the driver installer will be once files are copied, or '' when nothing is available locally.
function LocalDriverPath(): String;
begin
  Result := ExpandConstant('{app}\redist\') + DriverFileName;
end;

// True when neither the embedded redist nor a redist\ folder next to the setup will provide the installer.
function NeedsDriverDownload(): Boolean;
begin
  Result := not IsDriverInstalled()
            and (DriverBundled = 0)
            and not FileExists(ExpandConstant('{src}\redist\') + DriverFileName);
end;

function OnDownloadProgress(const Url, FileName: String; const Progress, ProgressMax: Int64): Boolean;
begin
  if Progress = ProgressMax then
    Log(Format('Downloaded %s (%d bytes)', [FileName, ProgressMax]));
  Result := True;
end;

function VerifyDriverHash(const Path: String): Boolean;
var
  Actual: String;
begin
  Actual := GetSHA256OfFile(Path);
  Result := CompareText(Actual, DriverSha256) = 0;
  if not Result then
    Log(Format('SHA-256 mismatch for %s: expected %s, got %s', [Path, DriverSha256, Actual]));
end;

procedure InitializeWizard();
begin
  DownloadPage := CreateDownloadPage(CustomMessage('DriverDownloadTitle'),
    FmtMessage(CustomMessage('DriverDownloadDesc'), [DriverUrl]), @OnDownloadProgress);
  DownloadPage.ShowBaseNameInsteadOfUrl := True;
end;

// Download the driver to {tmp} (interactive, with the download page) and verify it. Returns True when the file is
// ready; otherwise LastDownloadError holds the reason.
function DownloadDriver(): Boolean;
var
  TmpPath: String;
begin
  Result := False;
  DownloadedDriverPath := '';
  LastDownloadError := '';
  TmpPath := ExpandConstant('{tmp}\') + DriverFileName;
  DownloadPage.Clear;
  // Inno verifies the hash itself; we re-check with GetSHA256OfFile before executing anyway.
  DownloadPage.Add(DriverUrl, DriverFileName, DriverSha256);
  DownloadPage.Show;
  try
    try
      DownloadPage.Download;
      if VerifyDriverHash(TmpPath) then
      begin
        DownloadedDriverPath := TmpPath;
        Result := True;
      end
      else
      begin
        DeleteFile(TmpPath);
        LastDownloadError := CustomMessage('DriverHashMismatch');
        SuppressibleMsgBox(CustomMessage('DriverHashMismatch'), mbCriticalError, MB_OK, IDOK);
      end;
    except
      // GetExceptionMessage is only meaningful inside the except block: capture it here.
      LastDownloadError := GetExceptionMessage;
      if DownloadPage.AbortedByUser then
        Log('Driver download aborted by user.')
      else
        Log('Driver download failed: ' + LastDownloadError);
    end;
  finally
    DownloadPage.Hide;
  end;
end;

// Silent-mode download (NextButtonClick is never called with /SILENT or /VERYSILENT): no wizard page, no prompts.
function DownloadDriverSilently(): Boolean;
var
  TmpPath: String;
begin
  Result := False;
  DownloadedDriverPath := '';
  LastDownloadError := '';
  TmpPath := ExpandConstant('{tmp}\') + DriverFileName;
  try
    Log('Silent install: downloading ' + DriverUrl);
    DownloadTemporaryFile(DriverUrl, DriverFileName, DriverSha256, @OnDownloadProgress);
    if VerifyDriverHash(TmpPath) then
    begin
      DownloadedDriverPath := TmpPath;
      Result := True;
    end
    else
    begin
      DeleteFile(TmpPath);
      LastDownloadError := CustomMessage('DriverHashMismatch');
      Log(LastDownloadError);
    end;
  except
    LastDownloadError := GetExceptionMessage;
    Log('Silent driver download failed: ' + LastDownloadError);
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Reason: String;
begin
  Result := True;
  if (CurPageID = wpReady) and NeedsDriverDownload() then
  begin
    if not DownloadDriver() then
    begin
      if DownloadPage.AbortedByUser then
        Reason := SetupMessage(msgErrorInternal2)
      else
        Reason := LastDownloadError;
      if Reason = '' then
        Reason := DriverUrl;
      // Let the user retry (No keeps the wizard on the Ready page) or continue without the driver.
      Result := SuppressibleMsgBox(FmtMessage(CustomMessage('DriverDownloadFailed'), [Reason]),
                  mbConfirmation, MB_YESNO, IDYES) = IDYES;
    end;
  end;
end;

// Called in silent mode too (unlike NextButtonClick). The interactive path has already downloaded by now.
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if WizardSilent() and (DownloadedDriverPath = '') and NeedsDriverDownload() then
  begin
    if not DownloadDriverSilently() then
      Log('Continuing the silent install without the driver; the application will offer to install it on first run.');
  end;
end;

procedure InstallDriver();
var
  Path: String;
  ResultCode: Integer;
begin
  if IsDriverInstalled() then
  begin
    Log('Parsec VDD already installed (devnode with HardwareID ' + DriverHardwareId + ' present); skipping driver setup.');
    exit;
  end;

  Path := LocalDriverPath();
  if not FileExists(Path) then
    Path := DownloadedDriverPath;
  if (Path = '') or not FileExists(Path) then
  begin
    Log('No Parsec VDD installer available; the application will offer to install it on first run.');
    exit;
  end;

  if not VerifyDriverHash(Path) then
  begin
    SuppressibleMsgBox(CustomMessage('DriverHashMismatch'), mbCriticalError, MB_OK, IDOK);
    exit;
  end;

  // Keep a downloaded (already verified) copy in {app}\redist so the in-app "Install driver" button does not
  // have to download it again ({tmp} is deleted when Setup exits; UninstallDelete removes {app}\redist).
  if (Path = DownloadedDriverPath) and (DownloadedDriverPath <> '') then
  begin
    if ForceDirectories(ExpandConstant('{app}\redist')) and FileCopy(DownloadedDriverPath, LocalDriverPath(), False) then
      Log('Downloaded driver kept at ' + LocalDriverPath())
    else
      Log('Could not keep a copy of the downloaded driver in {app}\redist: ' + SysErrorMessage(DLLGetLastError()));
  end;

  WizardForm.StatusLabel.Caption := CustomMessage('DriverStatus');
  WizardForm.FilenameLabel.Caption := Path;
  Log('Running ' + Path + ' /S');
  if Exec(Path, '/S', ExtractFileDir(Path), SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    Log(Format('Parsec VDD installer exit code: %d', [ResultCode]));
    if ResultCode <> 0 then
      SuppressibleMsgBox(FmtMessage(CustomMessage('DriverInstallFailed'), [IntToStr(ResultCode)]),
        mbError, MB_OK, IDOK)
    else if not IsDriverInstalled() then
      // The NSIS installer returned 0 but the device node is not there yet: Windows may finish it after a reboot.
      SuppressibleMsgBox(CustomMessage('DriverRestartRequired'), mbInformation, MB_OK, IDOK);
  end
  else
  begin
    Log(Format('Exec failed: %s (%d)', [SysErrorMessage(ResultCode), ResultCode]));
    SuppressibleMsgBox(FmtMessage(CustomMessage('DriverInstallFailed'), [IntToStr(ResultCode)]),
      mbError, MB_OK, IDOK);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    InstallDriver();
end;
