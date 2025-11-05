Instrucciones para añadir FFmpeg (x64) al proyecto TMRJW

Objetivo:
Colocar los binarios de FFmpeg (ffmpeg.exe y librerías necesarias) en una carpeta dentro del directorio de la aplicación para que la app pueda generar miniaturas de vídeo si MediaPlayer falla.

Requisitos:
- FFmpeg (build estático) para Windows x64.

Descarga recomendada (enlace oficial):
- https://ffmpeg.org/download.html

Alternativas con builds estáticos (más sencillas):
- Gyan: https://www.gyan.dev/ffmpeg/builds/
- BtbN:  https://github.com/BtbN/FFmpeg-Builds/releases

Pasos:
1) Descarga una build estática de FFmpeg para Windows x64 (release static/shared). Preferible: build "full" o "essentials".
2) Extrae el ZIP descargado. Localiza la carpeta `bin` que contiene `ffmpeg.exe` (y opcionalmente `ffprobe.exe`, DLLs).
3) Copia el contenido de esa carpeta `bin` en el directorio del proyecto:
   - Ruta destino sugerida dentro del repo: `ffmpeg/bin/x64/`
   - Es decir, después de copiar deberías tener: `ffmpeg/bin/x64/ffmpeg.exe`.
4) Reinicia la aplicación (si estaba en ejecución).
5) En la aplicación: abre `Ajustes` → en "Ruta a FFmpeg" pulsa "Buscar..." y selecciona el `ffmpeg.exe` que colocaste en `ffmpeg/bin/x64` (puedes seleccionar directamente `ffmpeg.exe` dentro de esa carpeta). Guarda los ajustes.

Notas:
- La app también intenta usar MediaPlayer para generar miniaturas; FFmpeg se usa solo como fallback cuando MediaPlayer no puede extraer un frame del vídeo.
- No incluyas los binarios en el control de versiones si no quieres aumentar el tamaño del repositorio; puedes colocar la carpeta localmente en la máquina donde ejecutes la app.
- Si prefieres mantener los binarios fuera del repo, simplemente selecciona la ruta al `ffmpeg.exe` descargado usando la UI de Ajustes (la app guardará la ruta en settings).

Soporte:
Si quieres que incluya los binarios directamente en el repo en una rama separada (branch) puedo hacerlo, pero esto aumentará el tamaño del repo. Confírmame si deseas que lo añada.
