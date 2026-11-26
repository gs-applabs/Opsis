#ifndef PreviewPayload
  #error Define PreviewPayload
#endif
#ifndef OutputFolder
  #define OutputFolder "."
#endif
[Setup]
AppId={{9C079287-0AE6-4E34-AC2B-FBD794A694C8}
AppName=OPSIS European Capitals - REVIEW PREVIEW
AppVersion=0.26.0
AppPublisher=GS App Labs
DefaultDirName={autopf}\Opsis - Desktop Customization
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputFolder}
OutputBaseFilename=OPSIS-European-Capitals-R26-Preview-Setup
Compression=lzma2/fast
SolidCompression=yes
UninstallFilesDir={app}\widgets\wallpaper\scenes\europe-preview\uninstall
UninstallDisplayName=OPSIS European Capitals - REVIEW PREVIEW R26
[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
[Files]
Source: "{#PreviewPayload}\*"; DestDir: "{app}\widgets\wallpaper\scenes\europe-preview"; Flags: ignoreversion recursesubdirs createallsubdirs
[Code]
function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = wpSelectDir then begin
    if not FileExists(ExpandConstant('{app}\Opsis.exe')) then begin
      MsgBox('Select the existing OPSIS installation folder containing Opsis.exe.', mbError, MB_OK);
      Result := False;
    end;
  end;
end;
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not FileExists(ExpandConstant('{app}\Opsis.exe')) then
    Result := 'Existing OPSIS.exe not found. Install OPSIS first.';
end;
