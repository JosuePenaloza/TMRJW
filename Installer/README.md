Cómo compilar el instalador

Requisitos:
- Inno Setup (https://jrsoftware.org/isinfo.php) instalado en Windows.
- La carpeta de publicación generada: `TMRJW/bin/Release/net10.0-windows/publish-win-x64`

Pasos:
1. Asegúrate de haber publicado la aplicación (self-contained) y de que la carpeta exista:
   ```
   dotnet publish TMRJW/TMRJW.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:UseAppHost=true -o TMRJW/bin/Release/net10.0-windows/publish-win-x64
   ```

2. Abre Inno Setup y carga `Installer/TMRJWInstaller.iss`, luego pulsa "Compile" para generar el instalador.
   Alternativamente, compílalo desde la línea de comandos (ruta típica del compilador):
   ```
   "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" "Installer\TMRJWInstaller.iss"
   ```

3. El instalador resultante `TMRJW-Setup.exe` se colocará en la carpeta indicada por `OutputDir` en el script (.iss). Por defecto se ha configurado `TMRJW\bin\Release`.

Firmado del instalador (opcional):
- Si cuentas con un certificado de firma de código (archivo .pfx), te recomiendo firmar el instalador para reducir advertencias de Windows SmartScreen y aumentar la confianza.
- Ejemplo de comando con SignTool (Windows SDK):
  ```
  signtool sign /f "ruta\a\cert.pfx" /p "CONTRASENA" /tr "http://timestamp.digicert.com" /td SHA256 /fd SHA256 "ruta\a\TMRJW-Setup.exe"
  ```
- Para automatizar la firma en un script o en CI necesitarás almacenar el .pfx y la contraseña de forma segura.

Personalización:
- Edita `SetupIconFile` en `TMRJWInstaller.iss` para cambiar el icono del instalador.
- Ajusta `DefaultDirName`, `DefaultGroupName` u otras opciones de la sección [Setup] según prefieras.
- Si quieres que el instalador incluya otros archivos adicionales, añádelos en la sección [Files].

Problemas comunes:
- Asegúrate de publicar la aplicación para la arquitectura correcta (win-x64 o win-x86) antes de compilar el instalador.
- Si Inno Setup no encuentra archivos, verifica las rutas relativas en el .iss y que la carpeta `publish-win-x64` esté completa.

Si quieres, puedo:
- Generar un script PowerShell para firmar el instalador usando un .pfx local.
- Sugerir un flujo de trabajo (GitHub Actions) para construir, firmar y publicar el instalador automáticamente.
