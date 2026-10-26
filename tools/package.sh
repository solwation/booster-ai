#!/usr/bin/env bash
# Builds release archives and checksums into dist/release/ (used by the release workflow).
#   tools/package.sh 0.2.0     # or v0.2.0
# Produces BoosterAI-<version>-win-x64.zip, BoosterAI-<version>-linux-x64.tar.gz, SHA256SUMS.
set -euo pipefail
cd "$(dirname "$0")/.."
ver="${1:?usage: tools/package.sh <version>}"
ver="${ver#v}"
out="$PWD/dist/release"
rm -rf "$out" dist/win-x64 dist/linux-x64
mkdir -p "$out"

VERSION="$ver" tools/publish.sh win-x64 linux-x64

# Windows: exe + installer script. The script finds the exe next to itself.
win="dist/stage/BoosterAI-$ver-win-x64"
rm -rf dist/stage
mkdir -p "$win"
cp dist/win-x64/BoosterAI.exe tools/install-windows.ps1 "$win/"
(cd dist/stage && python3 -m zipfile -c "$out/BoosterAI-$ver-win-x64.zip" "BoosterAI-$ver-win-x64")

# Linux: binary, installer, icons. The script finds them next to itself.
lin="dist/stage/BoosterAI-$ver-linux-x64"
mkdir -p "$lin"
cp dist/linux-x64/BoosterAI tools/install-linux.sh assets/boosterai.svg assets/boosterai.png "$lin/"
chmod 755 "$lin/BoosterAI" "$lin/install-linux.sh"
tar -C dist/stage -czf "$out/BoosterAI-$ver-linux-x64.tar.gz" "BoosterAI-$ver-linux-x64"

(cd "$out" && sha256sum BoosterAI-* > SHA256SUMS)
ls -l "$out"
