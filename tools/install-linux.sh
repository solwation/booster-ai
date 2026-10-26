#!/usr/bin/env bash
# Installs dist/linux-x64/BoosterStart for the current user with a menu entry.
set -euo pipefail
cd "$(dirname "$0")/.."
src=dist/linux-x64/BoosterStart
[ -x "$src" ] || { echo "Bygg först: tools/publish.sh linux-x64"; exit 1; }
install -Dm755 "$src" ~/.local/bin/BoosterStart
install -Dm644 assets/boosterstart.png ~/.local/share/icons/hicolor/256x256/apps/boosterstart.png
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
