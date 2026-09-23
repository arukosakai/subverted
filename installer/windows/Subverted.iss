; Per-user installer for the Windows bundle: no admin prompt, and `sv` goes on the user's PATH.
; Built by .github/actions/windows-installer:
;   iscc /DAppVersion=0.1.0 /DSourceDir=<bundle dir> /DOutputDir=<dir> Subverted.iss

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #error Pass /DSourceDir=<published bundle directory>
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif

[Setup]
AppId={{20895B41-B48B-4552-B6D6-BD46E46DCFA6}
AppName=Subverted
AppVersion={#AppVersion}
AppPublisher=Subverted
AppPublisherURL=https://github.com/arukosakai/subverted
DefaultDirName={localappdata}\Programs\Subverted
DefaultGroupName=Subverted
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ChangesEnvironment=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\Subverted.exe
OutputDir={#OutputDir}
OutputBaseFilename=subverted-{#AppVersion}-win-x64-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
LicenseFile=..\..\LICENSE

[Tasks]
Name: addtopath; Description: "Add &sv to PATH"
Name: desktopicon; Description: "Create a &desktop shortcut"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Subverted"; Filename: "{app}\Subverted.exe"
Name: "{userdesktop}\Subverted"; Filename: "{app}\Subverted.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Subverted.exe"; Description: "Launch Subverted"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; The daemon holds its own executable open; stopping it first lets the files be removed.
Filename: "{app}\sv.exe"; Parameters: "daemon stop"; Flags: runhidden waituntilterminated; RunOnceId: "StopDaemon"

[Code]
const
  EnvironmentKey = 'Environment';

{ Position of Dir in the ;-separated Paths, 0 when absent. Case-insensitive, as Windows paths are. }
function PathEntryPosition(Paths, Dir: string): Integer;
begin
  Result := Pos(';' + Uppercase(Dir) + ';', ';' + Uppercase(Paths) + ';');
end;

procedure AddToUserPath(Dir: string);
var
  Paths: string;
begin
  if not RegQueryStringValue(HKCU, EnvironmentKey, 'Path', Paths) then
    Paths := '';
  if PathEntryPosition(Paths, Dir) > 0 then
    exit;
  if (Paths <> '') and (Copy(Paths, Length(Paths), 1) <> ';') then
    Paths := Paths + ';';
  RegWriteExpandStringValue(HKCU, EnvironmentKey, 'Path', Paths + Dir);
end;

procedure RemoveFromUserPath(Dir: string);
var
  Paths: string;
  Position: Integer;
begin
  if not RegQueryStringValue(HKCU, EnvironmentKey, 'Path', Paths) then
    exit;
  Position := PathEntryPosition(Paths, Dir);
  if Position = 0 then
    exit;
  { Take one separator with the entry: the one before it, or after it when it comes first. }
  if Position > 1 then
    Delete(Paths, Position - 1, Length(Dir) + 1)
  else
    Delete(Paths, Position, Length(Dir) + 1);
  RegWriteExpandStringValue(HKCU, EnvironmentKey, 'Path', Paths);
end;

{ An upgrade overwrites the daemon's executable, which Windows refuses while it runs. }
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExistingCli: string;
  ResultCode: Integer;
begin
  ExistingCli := ExpandConstant('{app}\sv.exe');
  if FileExists(ExistingCli) then
    Exec(ExistingCli, 'daemon stop', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('addtopath') then
    AddToUserPath(ExpandConstant('{app}'));
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    RemoveFromUserPath(ExpandConstant('{app}'));
end;
