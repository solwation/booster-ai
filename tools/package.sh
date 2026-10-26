#!/usr/bin/env bash
# Builds release archives and checksums into dist/release/ (used by the release workflow).
#   tools/package.sh 0.2.0     # or v0.2.0
# Produces BoosterStart-<version>-win-x64.zip, BoosterStart-<version>-linux-x64.tar.gz, SHA256SUMS.
set -euo pipefail
cd "$(dirname "$0")/.."
ver="${1:?usage: tools/package.sh <version>}"
ver="${ver#v}"
out="$PWD/dist/release"
rm -rf "$out" dist/win-x64 dist/linux-x64
mkdir -p "$out"

VERSION="$ver" tools/publish.sh win-x64 linux-x64

# Windows: exe + installer script. The script finds the exe next to itself.
win="dist/stage/BoosterStart-$ver-win-x64"
rm -rf dist/stage
mkdir -p "$win"
cp dist/win-x64/BoosterStart.exe tools/install-windows.ps1 "$win/"
(cd dist/stage && python3 -m zipfile -c "$out/BoosterStart-$ver-win-x64.zip" "BoosterStart-$ver-win-x64")

# Linux: binary, installer, icons. The script finds them next to itself.
lin="dist/stage/BoosterStart-$ver-linux-x64"
mkdir -p "$lin"
cp dist/linux-x64/BoosterStart tools/install-linux.sh assets/boosterstart.svg assets/boosterstart.png "$lin/"
chmod 755 "$lin/BoosterStart" "$lin/install-linux.sh"
tar -C dist/stage -czf "$out/BoosterStart-$ver-linux-x64.tar.gz" "BoosterStart-$ver-linux-x64"

(cd "$out" && sha256sum BoosterStart-* > SHA256SUMS)
ls -l "$out"
