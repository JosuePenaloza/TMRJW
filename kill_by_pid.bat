@echo off
REM Reemplaza 264 por el PID que indica tu error si cambia
tasklist /FI "IMAGENAME eq TMRJW.exe" /FI "PID eq 264"
taskkill /PID 264 /F
if %ERRORLEVEL% EQU 0 ( echo Proceso detenido ) else ( echo No se pudo detener o no existe )