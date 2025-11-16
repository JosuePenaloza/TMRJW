; Inno Setup script generated for TMRJW
; Place this file in the Installer folder and compile with ISCC.exe (Inno Setup Compiler)

[Setup]
AppName=TMRJW
AppVersion=1.0
DefaultDirName={pf}\TMRJW
DefaultGroupName=TMRJW
OutputBaseFilename=TMRJW-Setup
OutputDir=..\TMRJW\bin\Release
Compression=lzma
SolidCompression=yes
SetupIconFile=..\TMRJW\Assets\app.ico
DisableDirPage=no
DisableProgramGroupPage=no
LicenseFile=

[Tasks]
Name: "desktopicon"; Description: "Crear un &acceso directo en el Escritorio"; GroupDescription: "Iconos adicionales:"; Flags: unchecked

[Files]
; Copiar toda la carpeta de publicación (publicación self-contained)
Source: "..\TMRJW\bin\Release\net10.0-windows\publish-win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\TMRJW"; Filename: "{app}\TMRJW.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\TMRJW"; Filename: "{app}\TMRJW.exe"; Tasks: desktopicon; WorkingDir: "{app}"

[Run]
; Ejecutar la aplicación tras la instalación (opcional)
Filename: "{app}\TMRJW.exe"; Description: "Iniciar TMRJW"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
