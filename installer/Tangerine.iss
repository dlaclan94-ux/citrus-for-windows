; 탠저린 설치 파일 (Inno Setup 6). 배포 흐름(.github/workflows/release.yml)이 만든다.
;   iscc /DAppVersion=1.0.0 /DSourceDir=..\out\Tangerine installer\Tangerine.iss
; 내 계정에만 설치해 관리자 권한이 필요 없다. scripts\install-local.ps1 과 같은 위치·같은 자동 실행 값을 쓴다.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\out\Tangerine"
#endif

[Setup]
AppId={{D1D4FA32-71A5-4658-B636-5235F33F5CE5}
AppName=Tangerine for Windows
AppVerName=탠저린 {#AppVersion}
AppVersion={#AppVersion}
AppPublisher=dlaclan
AppPublisherURL=https://github.com/dlaclan94-ux/tangerine-for-windows
AppComments=끌어서 바로 바꾸는 파일 변환기. 맥용 Tangerine(Leap Studio)을 참고해 만든 개인 프로젝트.
DefaultDirName={localappdata}\Programs\Tangerine
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir=..\out
OutputBaseFilename=Tangerine-Setup-v{#AppVersion}
SetupIconFile=..\src\Tangerine\Tangerine.ico
UninstallDisplayIcon={app}\Tangerine.exe
UninstallDisplayName=Tangerine for Windows (탠저린)
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
; 켜져 있는 탠저린은 끄고 바꾼다
CloseApplications=force
RestartApplications=no

[Languages]
#if FileExists(CompilerPath + "Languages\Korean.isl")
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
#else
Name: "english"; MessagesFile: "compiler:Default.isl"
#endif

[Tasks]
Name: "desktopicon"; Description: "바탕화면에 바로가기 만들기"
Name: "startup"; Description: "Windows 에 로그인할 때 자동으로 실행"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs
Source: "..\docs\tangerine-guide.pdf"; DestDir: "{app}"; DestName: "탠저린 설명서.pdf"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\탠저린"; Filename: "{app}\Tangerine.exe"
Name: "{userprograms}\탠저린 설명서"; Filename: "{app}\탠저린 설명서.pdf"
Name: "{userdesktop}\탠저린"; Filename: "{app}\Tangerine.exe"; Tasks: desktopicon

[Registry]
; 알림 영역 메뉴의 "Windows 시작 시 실행" 과 같은 자리라 그 체크로 끄고 켤 수 있다
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Tangerine"; ValueData: """{app}\Tangerine.exe"""; Tasks: startup; Flags: uninsdeletevalue
; 설치 뒤 알림 영역 메뉴로 켠 자동 실행 값과, 한글 변환 때 적는 보안 모듈 위치도 지울 때 함께 지운다 (설치 때는 만들지 않음)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "Tangerine"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\HNC\HwpAutomation\Modules"; ValueType: none; ValueName: "TangerineFilePathChecker"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\Tangerine.exe"; Description: "탠저린 실행"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; 지우기 전에 켜져 있는 탠저린을 끈다
Filename: "{sys}\taskkill.exe"; Parameters: "/IM Tangerine.exe /F"; Flags: runhidden; RunOnceId: "StopTangerine"
