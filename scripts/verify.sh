#!/usr/bin/env bash
# The single "is it green?" gate. Agents MUST run this before claiming work is done.
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet build --nologo -v quiet -clp:ErrorsOnly
if ! dotnet ef migrations has-pending-model-changes --project src/AmpersandOfTruth.Web --no-build >/dev/null 2>&1; then
  echo "❌ EF model changed without a migration. Run:"
  echo "   dotnet ef migrations add <Name> --project src/AmpersandOfTruth.Web --output-dir Data/Migrations"
  exit 1
fi
dotnet test --nologo --no-build -v quiet "$@"
echo "✅ verify passed"
