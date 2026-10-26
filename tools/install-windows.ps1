# Installs BoosterAI.exe (next to this script in a release download, else dist\win-x64\) for the current user (no admin needed):
# %LOCALAPPDATA%\Programs\BoosterAI plus Start menu and desktop shortcuts.
#   powershell -ExecutionPolicy Bypass -File tools\install-windows.ps1
$ErrorActionPreference = 'Stop'
$src = Join-Path $PSScriptRoot 'BoosterAI.exe'
if (-not (Test-Path $src)) { $src = Join-Path $PSScriptRoot '..\dist\win-x64\BoosterAI.exe' }
if (-not (Test-Path $src)) { throw "Bygg först: tools/publish.sh win-x64 (saknar $src)" }
# In a checkout, refuse a dist build older than the sources: installing it would silently ship the previous version.
$srcDir = Join-Path $PSScriptRoot '..\src\BoosterAI'
if (Test-Path -LiteralPath $srcDir) {
  $newest = Get-ChildItem -LiteralPath $srcDir -Recurse -File |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } | Sort-Object LastWriteTime | Select-Object -Last 1
  if ($newest -and $newest.LastWriteTime -gt (Get-Item -LiteralPath $src).LastWriteTime) {
    throw "dist-bygget är äldre än $($newest.Name); kör tools/publish.sh win-x64 först."
  }
}

# Remove the pre-rename install (BoosterStart): stop it, delete its folder and shortcuts. Literal paths only.
$oldDir = Join-Path $env:LOCALAPPDATA 'Programs\BoosterStart'
# Only the installed copy: a process whose Path is unknown or elsewhere is left alone.
$old = Get-Process BoosterStart -ErrorAction SilentlyContinue |
  Where-Object { $_.Path -and $_.Path.StartsWith("$oldDir\", [StringComparison]::OrdinalIgnoreCase) }
$old | Stop-Process -Force
$old | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
# The folder can stay locked as the working directory of a terminal the old app started;
# then only its files go and the empty folder is left for later.
if (Test-Path -LiteralPath $oldDir) {
  try { Remove-Item -LiteralPath $oldDir -Recurse -Force }
  catch { Write-Warning "Kunde inte ta bort $oldDir helt (används av en annan process); ta bort mappen senare." }
}
foreach ($folder in @([Environment]::GetFolderPath('Programs'), [Environment]::GetFolderPath('Desktop'))) {
  $oldLnk = Join-Path $folder 'BoosterStart.lnk'
  if (Test-Path -LiteralPath $oldLnk) { Remove-Item -LiteralPath $oldLnk -Force }
}

$dir = Join-Path $env:LOCALAPPDATA 'Programs\BoosterAI'
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$dest = Join-Path $dir 'BoosterAI.exe'
# The installed copy locks the exe; stop it and wait until the file is free. Copies run from
# elsewhere (a desktop copy, an unpacked release) do not lock it and keep running.
$running = Get-Process BoosterAI -ErrorAction SilentlyContinue |
  Where-Object { $_.Path -and [string]::Equals($_.Path, $dest, [StringComparison]::OrdinalIgnoreCase) }
$running | Stop-Process -Force
$running | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
# The file can stay locked for a moment after the process exits (AV scan, handle teardown): retry, then fail clearly.
$copied = $false
for ($i = 1; $i -le 10 -and -not $copied; $i++) {
  try { Copy-Item -LiteralPath $src -Destination $dest -Force; $copied = $true }
  catch [System.IO.IOException] {
    if ($i -eq 10) { throw "Kunde inte ersätta $dest efter 10 försök (filen används av en annan process). Stäng BoosterAI och kör igen." }
    Start-Sleep -Milliseconds 500
  }
}

$shell = New-Object -ComObject WScript.Shell
foreach ($folder in @([Environment]::GetFolderPath('Programs'), [Environment]::GetFolderPath('Desktop'))) {
  $lnk = $shell.CreateShortcut((Join-Path $folder 'BoosterAI.lnk'))
  $lnk.TargetPath = Join-Path $dir 'BoosterAI.exe'
  $lnk.WorkingDirectory = $dir
  $lnk.Description = 'Starta arbetsflikar med AI-CLI'
  $lnk.Save()
}
Write-Host "Installerat i $dir (Start-menyn och skrivbordet)."
