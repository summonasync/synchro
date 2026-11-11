#!/usr/bin/env bash
# Release builds from Ubuntu: linux-x64 + win-x64 self-contained.
# Usage: scripts/release.sh [output-dir]   (default: dist/)
set -euo pipefail

OUT=$(realpath "${1:-dist}")
VERSION=$(git describe --tags --abbrev=0 2>/dev/null || echo "v0.1.0")
NUMVER=${VERSION#v}
PROJECT=src/Synchro.App/Synchro.App.csproj

rm -rf "$OUT"
mkdir -p "$OUT"

echo "== building linux-x64 =="
dotnet publish "$PROJECT" -c Release -r linux-x64 --self-contained \
  -p:PublishSingleFile=true -p:Version="$NUMVER" -p:PublishDir="$OUT/linux-x64/" >/dev/null

echo "== building win-x64 (single-file) =="
dotnet publish "$PROJECT" -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:Version="$NUMVER" -p:PublishDir="$OUT/win-x64/" >/dev/null

# the ASP.NET shared framework drops ANCM/IIS modules and pdb next to the
# single-file binary — neither ships with a self-contained console tool
rm -f "$OUT"/linux-x64/*.pdb "$OUT"/win-x64/*.pdb \
      "$OUT"/linux-x64/*.dll "$OUT"/win-x64/*.dll

# a self-contained single-file release must be exactly one binary per RID —
# anything left behind would break at runtime on the peer machine, not here
for rid in linux-x64 win-x64; do
  expected=linux; [ "$rid" = win-x64 ] && expected=synchro.exe
  found=$(ls "$OUT/$rid")
  if [ "$found" != "synchro" ] && [ "$found" != "synchro.exe" ]; then
    echo "release integrity: unexpected files in $OUT/$rid: $found" >&2
    exit 1
  fi
done

echo "== sha256 manifest =="
cd "$OUT"
find . -type f \( -name "synchro" -o -name "synchro.exe" \) -print0 | sort -z |
  xargs -0 sha256sum > "sha256-$VERSION.txt"
cat "sha256-$VERSION.txt"

echo "== artifacts =="
ls -la linux-x64/ win-x64/
echo "release $VERSION -> $OUT/"
