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
"$DOTNET_BIN" test "$ROOT_DIR/tests/BodyLife.Crm.Tests/BodyLife.Crm.Tests.csproj" \
  --configuration "$CONFIGURATION" \
  --no-build \
  --no-restore \
  --nologo
"$DOTNET_BIN" test "$ROOT_DIR/tests/BodyLife.Crm.Infrastructure.Tests/BodyLife.Crm.Infrastructure.Tests.csproj" \
  --configuration "$CONFIGURATION" \
  --no-build \
  --no-restore \
  --nologo

PLAYWRIGHT_SCRIPT="$ROOT_DIR/tests/BodyLife.Crm.Ui.SmokeTests/bin/$CONFIGURATION/net10.0/playwright.ps1"
if [[ "${BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL:-0}" != "1" ]]; then
  if ! command -v pwsh >/dev/null 2>&1; then
    printf 'PowerShell is required to install Playwright browsers. Install pwsh or set BODYLIFE_SKIP_PLAYWRIGHT_BROWSER_INSTALL=1 when browsers are already available.\n' >&2
    exit 1
  fi

  if [[ ! -f "$PLAYWRIGHT_SCRIPT" ]]; then
    printf 'Playwright install script not found at %s. Ensure the UI smoke test project built successfully.\n' "$PLAYWRIGHT_SCRIPT" >&2
    exit 1
  fi

  if [[ "${CI:-false}" == "true" || "${PLAYWRIGHT_INSTALL_WITH_DEPS:-0}" == "1" ]]; then
    pwsh "$PLAYWRIGHT_SCRIPT" install --with-deps chromium
  else
    pwsh "$PLAYWRIGHT_SCRIPT" install chromium
  fi
fi

"$DOTNET_BIN" test "$ROOT_DIR/tests/BodyLife.Crm.Ui.SmokeTests/BodyLife.Crm.Ui.SmokeTests.csproj" \
  --configuration "$CONFIGURATION" \
  --no-build \
  --no-restore \
  --nologo
"$DOTNET_BIN" tool run dotnet-ef migrations list \
  --no-connect \
  --project "$ROOT_DIR/src/BodyLife.Crm.Infrastructure/BodyLife.Crm.Infrastructure.csproj" \
  --startup-project "$ROOT_DIR/src/BodyLife.Crm.Web/BodyLife.Crm.Web.csproj" \
  --context BodyLifeDbContext

printf 'BodyLife validation gate completed.\n'
