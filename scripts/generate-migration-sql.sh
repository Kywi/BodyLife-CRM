#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUTPUT_PATH="${1:-$ROOT_DIR/artifacts/migrations/bodylife-idempotent.sql}"
DOTNET_BIN="${DOTNET_BIN:-dotnet}"

mkdir -p "$(dirname "$OUTPUT_PATH")"

"$DOTNET_BIN" tool restore --tool-manifest "$ROOT_DIR/.config/dotnet-tools.json" >/dev/null
"$DOTNET_BIN" tool run dotnet-ef migrations script \
  --idempotent \
  --project "$ROOT_DIR/src/BodyLife.Crm.Infrastructure/BodyLife.Crm.Infrastructure.csproj" \
  --startup-project "$ROOT_DIR/src/BodyLife.Crm.Web/BodyLife.Crm.Web.csproj" \
  --context BodyLifeDbContext \
  --output "$OUTPUT_PATH"

printf 'Migration SQL written to %s\n' "$OUTPUT_PATH"
