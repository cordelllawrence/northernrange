#!/usr/bin/env bash
set -euo pipefail

RIDS=("win-x64" "osx-arm64" "osx-x64" "linux-x64")

# If arguments provided, use those as RIDs instead
if [ $# -gt 0 ]; then
  RIDS=("$@")
fi

echo "Building northernrange for: ${RIDS[*]}"
echo

for rid in "${RIDS[@]}"; do
  echo "=== $rid ==="
  dotnet publish -c Release -r "$rid" -o "publish/$rid"
  echo
done

echo "Build complete. Output:"
echo
for rid in "${RIDS[@]}"; do
  if [ "$rid" = "win-x64" ]; then
    bin="publish/$rid/nr.exe"
  else
    bin="publish/$rid/nr"
  fi
  if [ -f "$bin" ]; then
    size=$(du -h "$bin" | cut -f1)
    echo "  $rid: $bin ($size)"
  else
    echo "  $rid: $bin (not found)"
  fi
done
