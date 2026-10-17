#!/bin/sh
set -eu
ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
for RID in win-x64 linux-x64 linux-arm64 linux-arm; do
    OUT="$ROOT/artifacts/$RID"
    dotnet publish "$ROOT/LanShare.csproj" -c Release -r "$RID" --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o "$OUT"
    python3 "$ROOT/scripts/package.py" --rid "$RID"
done
