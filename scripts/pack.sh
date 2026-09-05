#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
line="${1:-all}"
output="${2:-$ROOT_DIR/artifacts}"
case "$line" in
    all) lines=(10 11) ;;
    10|11) lines=("$line") ;;
    *) echo "Usage: scripts/pack.sh [all|10|11] [output-directory]" >&2; exit 2 ;;
esac
mkdir -p "$output"
output="$(cd "$output" && pwd)"

for major in "${lines[@]}"; do
    dotnet pack "$ROOT_DIR/DuckDB.EFCoreProvider.slnx" -c Release \
        -p:DuckDBEFCoreMajorVersion="$major" -o "$output/ef$major"
done
