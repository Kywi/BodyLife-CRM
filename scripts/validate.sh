#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET_BIN="${DOTNET_BIN:-dotnet}"
CONFIGURATION="${CONFIGURATION:-Release}"

cd "$ROOT_DIR"

"$DOTNET_BIN" tool restore --tool-manifest "$ROOT_DIR/.config/dotnet-tools.json"
"$DOTNET_BIN" restore BodyLife.Crm.sln --nologo
"$DOTNET_BIN" build BodyLife.Crm.sln --configuration "$CONFIGURATION" --no-restore --nologo
"$DOTNET_BIN" format BodyLife.Crm.sln --verify-no-changes --verbosity minimal --no-restore
"$DOTNET_BIN" tool run dotnet-ef migrations list \
  --no-connect \
  --project "$ROOT_DIR/src/BodyLife.Crm.Infrastructure/BodyLife.Crm.Infrastructure.csproj" \
  --startup-project "$ROOT_DIR/src/BodyLife.Crm.Web/BodyLife.Crm.Web.csproj" \
  --context BodyLifeDbContext

printf 'BodyLife validation gate completed.\n'
