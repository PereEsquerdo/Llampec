; Inno Setup script for a per-user Llampec installation (no administrator rights).
; Built by tools/package.ps1, which passes the published folder and version:
;   ISCC /DAppVersion=0.2.0-alpha.9 /DFileVersion=0.2.0.9 /DArch=arm64 /DSourceDir=<publish folder> /DOutputDir=<folder> Llampec.iss

#ifndef AppVersion
  #error Pass /DAppVersion=<version>.
#endif
#ifndef Arch
  #error Pass /DArch=x64 or /DArch=arm64.
#endif
#ifndef SourceDir
  #error Pass /DSourceDir=<published application folder>.
#endif
#ifndef FileVersion
  #define FileVersion "0.0.0.0"
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif

[Setup]
; Keep this AppId: it identifies Llampec for upgrades and uninstallation.
AppId={{6F3A9B52-6C1E-4E0A-9B7D-2F4C8E1A5D93}
AppName=Llampec
AppVersion={#AppVersion}
AppVerName=Llampec {#AppVersion}
AppPublisher=Pere Esquerdo Ramis
AppPublisherURL=https://github.com/PereEsquerdo/Llampec
AppSupportURL=https://github.com/PereEsquerdo/Llampec/issues
AppCopyright=Copyright (c) 2026 Pere Esquerdo Ramis
VersionInfoVersion={#FileVersion}
; Per user: {autopf} is %LOCALAPPDATA%\Programs and no elevation is requested. The folder is
; fixed and dedicated, so an upgrade may safely clear it (see [InstallDelete]).
PrivilegesRequired=lowest
DefaultDirName={autopf}\Llampec
DisableDirPage=yes
DisableProgramGroupPage=yes
#if Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
#endif
MinVersion=10.0.26100
OutputDir={#OutputDir}
OutputBaseFilename=Llampec-{#AppVersion}-win-{#Arch}-setup
SetupIconFile=..\src\Llampec.App\Assets\Llampec.ico
UninstallDisplayIcon={app}\Llampec.exe
UninstallDisplayName=Llampec
LicenseFile=..\LICENSE
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
; Llampec is closed through its own --exit, which restores pinned and fullscreen windows.
CloseApplications=no
ShowLanguageDialog=auto

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "catalan"; MessagesFile: "compiler:Languages\Catalan.isl"

[CustomMessages]
english.StartWithWindows=Start Llampec when I sign in to Windows
spanish.StartWithWindows=Iniciar Llampec al iniciar sesión en Windows
catalan.StartWithWindows=Inicia Llampec en iniciar la sessió a Windows
english.NeedsDotNet=Llampec needs the .NET 10 Runtime ({#Arch}), which was not found.%n%nOpen the download page now? Llampec will start once it is installed.
spanish.NeedsDotNet=Llampec necesita .NET 10 Runtime ({#Arch}) y no se ha encontrado.%n%n¿Abrir ahora la página de descarga? Llampec funcionará cuando esté instalado.
catalan.NeedsDotNet=Llampec necessita .NET 10 Runtime ({#Arch}) i no s'ha trobat.%n%nVols obrir ara la pàgina de baixada? Llampec funcionarà quan estiga instal·lat.

[Tasks]
Name: "startup"; Description: "{cm:StartWithWindows}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
; An upgrade replaces the whole application folder, so no file of an older version remains.
Type: filesandordirs; Name: "{app}\*"

[UninstallDelete]
Type: dirifempty; Name: "{app}"

[Icons]
Name: "{autoprograms}\Llampec"; Filename: "{app}\Llampec.exe"

[Registry]
; The same per-user Run value that Llampec's own "Start with Windows" setting writes.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Llampec"; ValueData: """{app}\Llampec.exe"" --background"; Tasks: startup
; Uninstallation removes it only when it starts this copy (see CurUninstallStepChanged).

[Run]
Filename: "{app}\Llampec.exe"; Description: "{cm:LaunchProgram,Llampec}"; Flags: nowait postinstall skipifsilent


[Code]
function DotNetRuntimeInstalled(): Boolean;
var
  Found: TFindRec;
begin
  { Framework-dependent: the runtime of this architecture lives in the native dotnet folder. }
  Result := FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.NETCore.App\10.*'), Found);
  if Result then FindClose(Found);
end;

function InitializeSetup(): Boolean;
var
  Code: Integer;
begin
  Result := True;
  if not DotNetRuntimeInstalled() and not WizardSilent() then
    if SuppressibleMsgBox(CustomMessage('NeedsDotNet'), mbConfirmation, MB_YESNO, IDNO) = IDYES then
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/10.0', '', '', SW_SHOWNORMAL, ewNoWait, Code);
end;

function LlampecRunning(): Boolean;
var
  Locator, Wmi, Processes: Variant;
begin
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Wmi := Locator.ConnectServer('.', 'root\CIMV2');
    Processes := Wmi.ExecQuery('SELECT ProcessId FROM Win32_Process WHERE Name = ''Llampec.exe''');
    Result := Processes.Count > 0;
  except
    Result := False;
  end;
end;

{ --exit only signals the running copy. Its normal shutdown restores pinned and fullscreen
  windows and the mouse, which can take several seconds; wait up to 30 seconds for it. }
procedure CloseLlampec(const Path: String);
var
  Code, Waited: Integer;
begin
  if not FileExists(Path) then Exit;
  Exec(Path, '--exit', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Waited := 0;
  while LlampecRunning() and (Waited < 30000) do
  begin
    Sleep(250);
    Waited := Waited + 250;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  CloseLlampec(ExpandConstant('{app}\Llampec.exe'));
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Command: String;
begin
  { Remove the startup entry however it was enabled, but never one that starts another copy. }
  if (CurUninstallStep = usPostUninstall) and
     RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Llampec', Command) and
     (Pos(Lowercase(ExpandConstant('{app}\Llampec.exe')), Lowercase(Command)) > 0) then
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Llampec');
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  CloseLlampec(ExpandConstant('{app}\Llampec.exe'));
end;
