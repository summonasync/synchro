#!/usr/bin/env bash
# Documented smoke: full two-sided round trip using only the documented
# CLI surface. Exit 0 = the agent workflow (init -> baseline -> edit ->
# apply -> return trip -> conflict -> resolution) completes end-to-end.
set -euo pipefail

[ "${SMOKE_DEBUG:-0}" = "1" ] && set -x

S=${SYNCHRO_BIN:?set SYNCHRO_BIN to the synchro binary path}
WORK=$(mktemp -d /tmp/synchro-smoke-XXXXXX)
trap 'rm -rf "$WORK"' EXIT
step() { echo "== $* =="; }

mkdir "$WORK/sideA" "$WORK/sideB"
cd "$WORK/sideA"

step "side A init + key ritual"
KEY=$(dotnet "$S" init --peer sideA | grep -E '^[0-9a-f]{64}$')
echo "v1 feature" > feature.cs
step "side A baseline pack"
dotnet "$S" pack --full >/dev/null

cd "$WORK/sideB"
step "side B init + apply baseline"
dotnet "$S" init --key "$KEY" --peer sideB >/dev/null
out=$(dotnet "$S" apply "$(ls -t ../sideA/synchro-*.synchr | head -1)"); echo "$out" | grep -q "applied"
grep -q "v1 feature" feature.cs

# side B works and packs the return trip
step "side B rework + pack"
echo "v1 feature + B rework" > feature.cs
echo "b only" > bfile.txt
dotnet "$S" pack >/dev/null

cd "$WORK/sideA"
step "side A applies the return trip (B's first package is a baseline: content conflict expected)"
out=$(dotnet "$S" apply "$(ls -t ../sideB/synchro-*.synchr | head -1)"); echo "$out" | grep -q "CONFLICTS"
grep -q "B rework" .synchro/scene/*/theirs/feature.cs

step "side A resolves: replace on feature.cs"
dotnet "$S" scene resolve feature.cs replace
out=$(dotnet "$S" scene commit); echo "$out" | grep -q "sync state advanced"
grep -q "B rework" feature.cs
grep -q "b only" bfile.txt

# conflict drill: both sides touch bfile.txt
cd "$WORK/sideB"
echo "B final" > bfile.txt
dotnet "$S" pack >/dev/null
cd "$WORK/sideA"
echo "A final" > bfile.txt
step "conflict drill"
out=$(dotnet "$S" apply "$(ls -t ../sideB/synchro-*.synchr | head -1)"); echo "$out" | grep -q "CONFLICTS"

dotnet "$S" scene | grep -q "bfile.txt"
step "resolve replace + commit"
dotnet "$S" scene resolve bfile.txt replace
out=$(dotnet "$S" scene commit); echo "$out" | grep -q "sync state advanced"
grep -q "B final" bfile.txt

echo "SMOKE OK — full round trip with conflict resolution completed"
