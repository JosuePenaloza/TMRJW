# Ejecutar en PowerShell (como administrador si es necesario).
# Detiene cualquier proceso llamado "TMRJW" y libera el .exe.
Get-Process -Name TMRJW -ErrorAction SilentlyContinue | ForEach-Object {
  Write-Output "Deteniendo TMRJW (PID=$($_.Id))..."
  Stop-Process -Id $_.Id -Force
}