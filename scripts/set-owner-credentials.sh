#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET="${DOTNET_BIN:-dotnet}"
CONFIGURATION="${CONFIGURATION:-Debug}"

if [ -z "${BODYLIFE_OWNER_LOGIN_NAME:-}" ]; then
  echo "BODYLIFE_OWNER_LOGIN_NAME must be set." >&2
  exit 64
fi

if [ -z "${BODYLIFE_OWNER_PASSWORD:-}" ]; then
  echo "BODYLIFE_OWNER_PASSWORD must be set." >&2
  echo "No default Owner credentials are created." >&2
  exit 64
fi

"${DOTNET}" run \
  --project "${ROOT_DIR}/src/BodyLife.Crm.Web/BodyLife.Crm.Web.csproj" \
  --configuration "${CONFIGURATION}" \
  --no-launch-profile \
  -- set-owner-credentials
