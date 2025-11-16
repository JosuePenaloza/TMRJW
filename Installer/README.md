How to build the installer

Requirements:
- Inno Setup (https://jrsoftware.org/isinfo.php) installed on Windows.
- The published app folder: TMRJW/bin/Release/net10.0-windows/publish-win-x64

Steps:
1. Ensure you've published the app (self-contained) and the folder exists.
   dotnet publish TMRJW/TMRJW.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:UseAppHost=true -o TMRJW/bin/Release/net10.0-windows/publish-win-x64

2. Open Inno Setup and compile `Installer/TMRJWInstaller.iss` or run the compiler from command line:
   "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" "Installer\TMRJWInstaller.iss"

3. The resulting installer `TMRJW-Setup.exe` will be located in the `OutputDir` configured in the .iss (by default `TMRJW\bin\Release`).

Customization:
- Edit `SetupIconFile` in the .iss if you want a different icon.
- Adjust `DefaultDirName` or add more files under [Files] as needed.
