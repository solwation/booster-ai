# Installs BoosterStart.exe (next to this script in a release download, else dist\win-x64\) for the current user (no admin needed):
# %LOCALAPPDATA%\Programs\BoosterStart plus Start menu and desktop shortcuts.
#   powershell -ExecutionPolicy Bypass -File tools\install-windows.ps1
$ErrorActionPreference = 'Stop'
$src = Join-Path $PSScriptRoot 'BoosterStart.exe'
if (-not (Test-Path $src)) { $src = Join-Path $PSScriptRoot '..\dist\win-x64\BoosterStart.exe' }
if (-not (Test-Path $src)) { throw "Bygg först: tools/publish.sh win-x64 (saknar $src)" }

$dir = Join-Path $env:LOCALAPPDATA 'Programs\BoosterStart'
New-Item -ItemType Directory -Force -Path $dir | Out-Null
# A running copy locks the exe; stop it and wait until the file is free.
$running = Get-Process BoosterStart -ErrorAction SilentlyContinue
$running | Stop-Process -Force
$running | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
Copy-Item $src (Join-Path $dir 'BoosterStart.exe') -Force

$shell = New-Object -ComObject WScript.Shell
foreach ($folder in @([Environment]::GetFolderPath('Programs'), [Environment]::GetFolderPath('Desktop'))) {
  $lnk = $shell.CreateShortcut((Join-Path $folder 'BoosterStart.lnk'))
  $lnk.TargetPath = Join-Path $dir 'BoosterStart.exe'
  $lnk.WorkingDirectory = $dir
  $lnk.Description = 'Starta arbetsflikar med AI-CLI'
  $lnk.Save()
}
Write-Host "Installerat i $dir (Start-menyn och skrivbordet)."
