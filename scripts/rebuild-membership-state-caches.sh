#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET="${DOTNET_BIN:-dotnet}"
CONFIGURATION="${CONFIGURATION:-Debug}"

"${DOTNET}" run \
  --project "${ROOT_DIR}/src/BodyLife.Crm.Web/BodyLife.Crm.Web.csproj" \
  --configuration "${CONFIGURATION}" \
  --no-launch-profile \
  -- rebuild-membership-state-caches
