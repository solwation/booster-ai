#!/usr/bin/env bash
# Builds release archives and checksums into dist/release/ (used by the release workflow).
#   tools/package.sh 0.2.0     # or v0.2.0
# Produces BoosterAI-Setup-<version>-win-x64.exe, BoosterAI-<version>-win-x64.zip,
# BoosterAI-<version>-linux-x64.tar.gz, SHA256SUMS.
# SKIP_PUBLISH=1 packages the binaries already in dist/win-x64 and dist/linux-x64 (the release
# workflow signs the Windows files in between); a signed dist/win-x64/install-windows.ps1 is
# used instead of tools/install-windows.ps1 when present.
set -euo pipefail
cd "$(dirname "$0")/.."
ver="${1:?usage: tools/package.sh <version>}"
ver="${ver#v}"
out="$PWD/dist/release"
rm -rf "$out"
mkdir -p "$out"

if [ "${SKIP_PUBLISH:-}" = 1 ]; then
  for f in dist/win-x64/BoosterAI.exe dist/linux-x64/BoosterAI; do
    [ -f "$f" ] || { echo "SKIP_PUBLISH=1 but $f is missing" >&2; exit 1; }
  done
else
  rm -rf dist/win-x64 dist/linux-x64
  VERSION="$ver" tools/publish.sh win-x64 linux-x64
fi
installer=tools/install-windows.ps1
[ -f dist/win-x64/install-windows.ps1 ] && installer=dist/win-x64/install-windows.ps1

# Windows: exe + installer script. The script finds the exe next to itself.
win="dist/stage/BoosterAI-$ver-win-x64"
rm -rf dist/stage
mkdir -p "$win"
cp dist/win-x64/BoosterAI.exe "$installer" "$win/"
mkdir -p "$win/licenses/agent-icons"
cp assets/agents/LICENSE-*.txt assets/agents/README.md "$win/licenses/agent-icons/"
(cd dist/stage && python3 -m zipfile -c "$out/BoosterAI-$ver-win-x64.zip" "BoosterAI-$ver-win-x64")

# Windows setup: the same (already signed, when signing is on) exe under a BoosterAI-Setup name, which makes
# it open the install wizard. Authenticode lives inside the file, so the copy keeps the signature. The updater
# only downloads the exact BoosterAI-<ver>-win-x64.zip name, never this file.
cp dist/win-x64/BoosterAI.exe "$out/BoosterAI-Setup-$ver-win-x64.exe"

# Linux: binary, installer, icons. The script finds them next to itself.
lin="dist/stage/BoosterAI-$ver-linux-x64"
mkdir -p "$lin"
cp dist/linux-x64/BoosterAI tools/install-linux.sh assets/boosterai.svg assets/boosterai.png "$lin/"
mkdir -p "$lin/licenses/agent-icons"
cp assets/agents/LICENSE-*.txt assets/agents/README.md "$lin/licenses/agent-icons/"
chmod 755 "$lin/BoosterAI" "$lin/install-linux.sh"
tar -C dist/stage -czf "$out/BoosterAI-$ver-linux-x64.tar.gz" "BoosterAI-$ver-linux-x64"

(cd "$out" && sha256sum BoosterAI-* > SHA256SUMS)
ls -l "$out"
