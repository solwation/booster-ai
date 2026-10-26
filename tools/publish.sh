#!/usr/bin/env bash
# Builds self-contained single-file executables into dist/<rid>/.
#   tools/publish.sh            # win-x64 and linux-x64
#   tools/publish.sh linux-x64  # one runtime
# VERSION=1.2.3 stamps that version into the binary (default: the csproj's <Version>).
set -euo pipefail
cd "$(dirname "$0")/.."
for rid in "${@:-win-x64 linux-x64}"; do
  for r in $rid; do
    dotnet publish src/BoosterAI -c Release -r "$r" --self-contained \
      -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
      -p:EnableCompressionInSingleFile=true -p:DebugType=none ${VERSION:+-p:Version=$VERSION} -o "dist/$r"
  done
done
