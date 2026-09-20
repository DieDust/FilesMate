#ifndef PayloadDir
  #error PayloadDir must point to a self-contained publish directory.
#endif
#ifndef ReleaseDir
  #error ReleaseDir must be set.
#endif
#ifndef AppVersion
  #define AppVersion "1.1.47-preview.20260916"
#endif
#ifndef AppFileVersion
  #define AppFileVersion "1.1.47.0"
#endif

[Setup]
AppId={{A7238544-6934-4DD5-A828-887E8E2A40AA}
AppName=FilesMate
AppVersion={#AppVersion}
AppPublisher=FilesMate
VersionInfoVersion={#AppFileVersion}
DefaultDirName={localappdata}\Programs\FilesMate
DefaultGroupName=FilesMate
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.22621
OutputDir={#ReleaseDir}
OutputBaseFilename=FilesMate-Setup-{#AppVersion}-win-x64
SetupIconFile=..\src\FilesMate.App\Assets\Branding\FilesMate.ico
UninstallDisplayIcon={app}\FilesMate.App.exe
Compression=lzma2
SolidCompression=yes
MergeDuplicateFiles=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"; InfoBeforeFile: "Preview-Readme.en.txt"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"; InfoBeforeFile: "Preview-Readme.ja.txt"

[CustomMessages]
english.DesktopShortcut=Create a desktop shortcut
japanese.DesktopShortcut=デスクトップにショートカットを作成する
english.LaunchFilesMate=Launch FilesMate
japanese.LaunchFilesMate=FilesMate を起動する

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"; Flags: unchecked

[Files]
#ifdef PayloadFileManifest
#include PayloadFileManifest
#else
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
#endif

[Icons]
Name: "{group}\FilesMate"; Filename: "{app}\FilesMate.App.exe"; AppUserModelID: "FilesMate.App"
Name: "{group}\FilesMate Global Search"; Filename: "{app}\SearchHost\FilesMate.SearchHost.exe"; Parameters: "--show"
Name: "{autodesktop}\FilesMate"; Filename: "{app}\FilesMate.App.exe"; Tasks: desktopicon; AppUserModelID: "FilesMate.App"

[Run]
Filename: "{app}\SearchHost\FilesMate.SearchHost.exe"; Parameters: "--sync-startup"; Flags: runhidden waituntilterminated
Filename: "{app}\FilesMate.App.exe"; Description: "{cm:LaunchFilesMate}"; Flags: nowait postinstall skipifsilent
Filename: "{app}\SearchHost\FilesMate.SearchHost.exe"; Parameters: "--background"; Flags: nowait runhidden; Check: IsFilesMateUpdate
Filename: "{app}\FilesMate.App.exe"; Flags: nowait; Check: IsFilesMateUpdate

[UninstallRun]
Filename: "{app}\SearchHost\FilesMate.SearchHost.exe"; Parameters: "--remove-startup"; Flags: runhidden waituntilterminated skipifdoesntexist; RunOnceId: "RemoveSearchStartup"
Filename: "{app}\SearchHost\FilesMate.SearchHost.exe"; Parameters: "--stop"; Flags: runhidden waituntilterminated skipifdoesntexist; RunOnceId: "StopSearchHost"
Filename: "{app}\FilesMate.App.exe"; Parameters: "--unregister-folder-handler"; Flags: runhidden waituntilterminated; RunOnceId: "RestoreFolderHandler"

[Code]
function IsFilesMateUpdate(): Boolean;
begin
  Result := ExpandConstant('{param:FILESMATEUPDATE|0}') = '1';
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  HostPath: String;
  ResultCode: Integer;
begin
  Result := '';
  HostPath := ExpandConstant('{app}\SearchHost\FilesMate.SearchHost.exe');
  if FileExists(HostPath) then
    Exec(HostPath, '--stop', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;
