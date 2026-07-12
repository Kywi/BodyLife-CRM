#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET_BIN="${DOTNET_BIN:-dotnet}"
CONFIGURATION="${CONFIGURATION:-Debug}"
ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Development}"

"$DOTNET_BIN" tool restore --tool-manifest "$ROOT_DIR/.config/dotnet-tools.json" >/dev/null

ASPNETCORE_ENVIRONMENT="$ASPNETCORE_ENVIRONMENT" \
  "$DOTNET_BIN" tool run dotnet-ef database update \
  --project "$ROOT_DIR/src/BodyLife.Crm.Infrastructure/BodyLife.Crm.Infrastructure.csproj" \
  --startup-project "$ROOT_DIR/src/BodyLife.Crm.Web/BodyLife.Crm.Web.csproj" \
  --context BodyLifeDbContext \
  --configuration "$CONFIGURATION"

printf 'BodyLife EF Core migrations applied using the %s environment.\n' "$ASPNETCORE_ENVIRONMENT"
