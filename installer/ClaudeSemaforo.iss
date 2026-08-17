; Instalador do Claude Semáforo (Inno Setup 6).
; Compile pelo installer\publicar.ps1, que gera o publish e passa PublishDir.

#define AppName "Claude Semáforo"
#define AppVersion "1.2.0"
#define AppPublisher "gesuinox"
#define AppExe "ClaudeSemaforo.exe"
#define AppUrl "https://github.com/gesuinox/claude-semaforo"

#ifndef PublishDir
  #define PublishDir "..\..\publish"
#endif

#ifndef OutputDir
  #define OutputDir "."
#endif

[Setup]
; Não mude o AppId: é por ele que o Windows reconhece uma atualização da versão anterior.
AppId={{9EE1A2AC-C23A-4331-833B-714E7766DB00}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppSupportURL={#AppUrl}
VersionInfoVersion={#AppVersion}

; Instala na pasta do usuário: sem UAC, sem pedir senha de administrador.
PrivilegesRequired=lowest
DefaultDirName={autopf}\Claude Semaforo
DisableProgramGroupPage=yes
DisableDirPage=auto
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
SetupIconFile=..\src\ClaudeSemaforo\app.ico

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Fecha a barra antes de sobrescrever o executável.
CloseApplications=yes
RestartApplications=no

OutputDir={#OutputDir}
OutputBaseFilename=ClaudeSemaforo-{#AppVersion}-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "startup"; Description: "Iniciar o {#AppName} junto com o Windows"; GroupDescription: "Ao entrar no Windows:"
Name: "desktopicon"; Description: "Criar um atalho na área de trabalho"; GroupDescription: "Atalhos:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; A mesma chave que o menu "Iniciar com o Windows" do app usa — assim os dois
; sempre concordam sobre o estado da opção.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
    ValueType: string; ValueName: "ClaudeSemaforo"; ValueData: """{app}\{#AppExe}"""; \
    Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#AppExe}"; Description: "Abrir o {#AppName} agora"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; As preferências (posição, tema, fixação) saem junto na desinstalação.
Type: filesandordirs; Name: "{userappdata}\ClaudeSemaforo"
