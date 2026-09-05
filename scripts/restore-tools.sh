#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet tool restore --tool-manifest "$ROOT_DIR/.config/dotnet-tools.json"
dotnet tool restore --tool-manifest "$ROOT_DIR/.config/ef11/dotnet-tools.json"
