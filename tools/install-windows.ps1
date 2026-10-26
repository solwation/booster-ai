# Installs BoosterAI.exe (next to this script in a release download, else dist\win-x64\) for the current user (no admin needed):
# %LOCALAPPDATA%\Programs\BoosterAI plus Start menu and desktop shortcuts.
#   powershell -ExecutionPolicy Bypass -File tools\install-windows.ps1
$ErrorActionPreference = 'Stop'
$src = Join-Path $PSScriptRoot 'BoosterAI.exe'
if (-not (Test-Path $src)) { $src = Join-Path $PSScriptRoot '..\dist\win-x64\BoosterAI.exe' }
if (-not (Test-Path $src)) { throw "Bygg först: tools/publish.sh win-x64 (saknar $src)" }

# Remove the pre-rename install (BoosterStart): stop it, delete its folder and shortcuts. Literal paths only.
$old = Get-Process BoosterStart -ErrorAction SilentlyContinue
$old | Stop-Process -Force
$old | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
$oldDir = Join-Path $env:LOCALAPPDATA 'Programs\BoosterStart'
if (Test-Path -LiteralPath $oldDir) { Remove-Item -LiteralPath $oldDir -Recurse -Force }
foreach ($folder in @([Environment]::GetFolderPath('Programs'), [Environment]::GetFolderPath('Desktop'))) {
  $oldLnk = Join-Path $folder 'BoosterStart.lnk'
  if (Test-Path -LiteralPath $oldLnk) { Remove-Item -LiteralPath $oldLnk -Force }
}

$dir = Join-Path $env:LOCALAPPDATA 'Programs\BoosterAI'
New-Item -ItemType Directory -Force -Path $dir | Out-Null
# A running copy locks the exe; stop it and wait until the file is free.
$running = Get-Process BoosterAI -ErrorAction SilentlyContinue
$running | Stop-Process -Force
$running | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
Copy-Item $src (Join-Path $dir 'BoosterAI.exe') -Force

$shell = New-Object -ComObject WScript.Shell
foreach ($folder in @([Environment]::GetFolderPath('Programs'), [Environment]::GetFolderPath('Desktop'))) {
  $lnk = $shell.CreateShortcut((Join-Path $folder 'BoosterAI.lnk'))
  $lnk.TargetPath = Join-Path $dir 'BoosterAI.exe'
  $lnk.WorkingDirectory = $dir
  $lnk.Description = 'Starta arbetsflikar med AI-CLI'
  $lnk.Save()
}
Write-Host "Installerat i $dir (Start-menyn och skrivbordet)."
