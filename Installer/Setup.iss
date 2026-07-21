; PickleGit installer - Inno Setup script.
; Requires the Release build to exist first: see build-installer.ps1 for the one-step build.
;
; AppId is fixed forever - it's what lets a newer installer upgrade an existing install in place
; instead of creating a duplicate Start Menu entry / install folder. Never change it.

#define MyAppName "PickleGit"
#define MyAppExeName "PickleGit.exe"
#define MyAppPublisher "PickleGit"
#define MyAppSourceDir "..\PickleGit\bin\Release\net472"
#define MyAppVersion GetVersionNumbersString(MyAppSourceDir + "\" + MyAppExeName)

[Setup]
AppId={{6AFF2753-887A-4AFD-9D27-3DDC6C674487}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Per-user install - never needs admin/UAC, installs entirely under the current user's profile.
PrivilegesRequired=lowest
; PickleGit.exe targets AnyCPU (the .NET Framework CLR JITs it native for whichever Windows
; architecture it's running on), and the [Files] section below bundles LibGit2Sharp's native
; git2 binary for every Windows architecture (x86/x64/arm64) - so no architecture restriction here.
OutputDir=Output
OutputBaseFilename=PickleGit-Setup-{#MyAppVersion}
SetupIconFile=..\PickleGit\Resources\pickle.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; The generic wildcard excludes debug symbols and the *entire* "lib" folder outright (rather than
; excluding each unwanted subfolder individually) - LibGit2Sharp populates "lib" with one git2
; build per OS/arch unconditionally, and excluding matched files still leaves the now-empty
; directory behind with createallsubdirs. A second, explicit entry below re-adds just the Windows
; natives (lib\win32\*, covering x86/x64/arm64 for the AnyCPU build), so linux/osx never get their
; directories created at all.
Source: "{#MyAppSourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; \
    Excludes: "*.pdb,\lib\*"
Source: "{#MyAppSourceDir}\lib\win32\*"; DestDir: "{app}\lib\win32"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
