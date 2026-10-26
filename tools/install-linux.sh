#!/usr/bin/env bash
# Installs BoosterAI (next to this script in a release download, else dist/linux-x64/) for the current user with a menu entry.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
if [ -e "$here/BoosterAI" ]; then
  # Extracted release download: everything sits next to this script.
  src="$here/BoosterAI"; assets="$here"
else
  # Repo checkout: built by tools/publish.sh.
  src="$here/../dist/linux-x64/BoosterAI"; assets="$here/../assets"
fi
[ -x "$src" ] || { echo "Bygg först: tools/publish.sh linux-x64"; exit 1; }
# Remove the pre-rename install (BoosterStart); literal paths only.
rm -f ~/.local/bin/BoosterStart \
  ~/.local/share/applications/boosterstart.desktop \
  ~/.local/share/icons/hicolor/scalable/apps/boosterstart.svg \
  ~/.local/share/icons/hicolor/256x256/apps/boosterstart.png
install -Dm755 "$src" ~/.local/bin/BoosterAI
install -Dm644 "$assets/boosterai.svg" ~/.local/share/icons/hicolor/scalable/apps/boosterai.svg
install -Dm644 "$assets/boosterai.png" ~/.local/share/icons/hicolor/256x256/apps/boosterai.png
mkdir -p ~/.local/share/applications
cat > ~/.local/share/applications/boosterai.desktop <<DESKTOP
[Desktop Entry]
Type=Application
Name=BoosterAI
Comment=Starta arbetsflikar med AI-CLI
Exec=$HOME/.local/bin/BoosterAI
Icon=boosterai
Terminal=false
Categories=Development;Utility;
DESKTOP
echo "Installerat: ~/.local/bin/BoosterAI och menyposten BoosterAI."
