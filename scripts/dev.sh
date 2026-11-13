#!/usr/bin/env bash
# Hot-reload dev server on http://localhost:5172
set -euo pipefail
cd "$(dirname "$0")/.."
exec dotnet watch --project src/Junction.Web --launch-profile http --non-interactive
