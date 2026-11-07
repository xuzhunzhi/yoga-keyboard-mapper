; Inno Setup 6 script for the per-user installer. Built by tools\package.ps1:
;   ISCC.exe /DAppVersion=1.0.0 /DSourceDir=<publish folder> /O<output folder> installer\YogaKeyboardMapper.iss

#define AppName "Yoga Keyboard Mapper"
#define AppExe "YogaKeyboardMapper.exe"
#define AppUrl "https://github.com/xuzhunzhi/yoga-keyboard-mapper"
#define RunKey "Software\Microsoft\Windows\CurrentVersion\Run"

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceDir
  #error Pass /DSourceDir=<self-contained publish folder>
#endif

[Setup]
AppId={{8C3F6A2E-5B1D-4E7A-9F02-3D6C1B7E4A95}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=xuzhunzhi
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}
; Per-user by default (no UAC prompt): installs to %LOCALAPPDATA%\Programs.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
; Matches App.xaml.cs, so setup asks to close a running copy first.
AppMutex=Local\YogaKeyboardMapper.SingleInstance
SetupIconFile=..\src\YogaKeyboardMapper\Assets\YogaKeyboardMapper.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
OutputBaseFilename=YogaKeyboardMapper-{#AppVersion}-x64-setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ShowLanguageDialog=auto

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "zh"; MessagesFile: "Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
// The app's "start with Windows" switch writes an HKCU Run entry; drop it on uninstall
// if it points at this install. Settings in %LOCALAPPDATA%\YogaKeyboardMapper are kept.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Command: String;
begin
  if (CurUninstallStep = usUninstall) and
     RegQueryStringValue(HKEY_CURRENT_USER, '{#RunKey}', 'YogaKeyboardMapper', Command) and
     (Pos(Lowercase(ExpandConstant('{app}')), Lowercase(Command)) > 0) then
    RegDeleteValue(HKEY_CURRENT_USER, '{#RunKey}', 'YogaKeyboardMapper');
end;
