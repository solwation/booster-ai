#!/usr/bin/env bash
# Installs BoosterStart (next to this script in a release download, else dist/linux-x64/) for the current user with a menu entry.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
if [ -e "$here/BoosterStart" ]; then
  # Extracted release download: everything sits next to this script.
  src="$here/BoosterStart"; assets="$here"
else
  # Repo checkout: built by tools/publish.sh.
  src="$here/../dist/linux-x64/BoosterStart"; assets="$here/../assets"
fi
[ -x "$src" ] || { echo "Bygg först: tools/publish.sh linux-x64"; exit 1; }
install -Dm755 "$src" ~/.local/bin/BoosterStart
install -Dm644 "$assets/boosterstart.svg" ~/.local/share/icons/hicolor/scalable/apps/boosterstart.svg
install -Dm644 "$assets/boosterstart.png" ~/.local/share/icons/hicolor/256x256/apps/boosterstart.png
mkdir -p ~/.local/share/applications
cat > ~/.local/share/applications/boosterstart.desktop <<DESKTOP
[Desktop Entry]
Type=Application
Name=BoosterStart
Comment=Starta arbetsflikar med AI-CLI
Exec=$HOME/.local/bin/BoosterStart
Icon=boosterstart
Terminal=false
Categories=Development;Utility;
DESKTOP
echo "Installerat: ~/.local/bin/BoosterStart och menyposten BoosterStart."
