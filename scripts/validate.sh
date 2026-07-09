#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET_BIN="${DOTNET_BIN:-dotnet}"
CONFIGURATION="${CONFIGURATION:-Release}"

configure_local_postgres_test_connection() {
  if [[ -n "${BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING:-}" ]]; then
    return
  fi

  local appsettings_path="$ROOT_DIR/src/BodyLife.Crm.Web/appsettings.Development.json"
  if [[ ! -f "$appsettings_path" ]] || ! command -v pwsh >/dev/null 2>&1; then
    return
  fi

  local admin_database="${BODYLIFE_TEST_POSTGRES_ADMIN_DATABASE:-postgres}"
  local connection_string
  connection_string="$(APPSETTINGS_PATH="$appsettings_path" ADMIN_DATABASE="$admin_database" pwsh -NoLogo -NoProfile -NonInteractive -Command '
$ErrorActionPreference = "Stop"

$settings = Get-Content -LiteralPath $env:APPSETTINGS_PATH -Raw | ConvertFrom-Json
$connectionString = $settings.ConnectionStrings.BodyLifeTestAdmin
if ([string]::IsNullOrWhiteSpace($connectionString)) {
  return
}

$parts = [System.Collections.Generic.List[string]]::new()
$databaseWasSet = $false
foreach ($part in $connectionString -split ";") {
  if ([string]::IsNullOrWhiteSpace($part)) {
    continue
  }

  $keyValue = $part -split "=", 2
  if ($keyValue.Count -eq 2 -and $keyValue[0].Trim().Equals("Database", [System.StringComparison]::OrdinalIgnoreCase)) {
    $parts.Add("Database=$env:ADMIN_DATABASE")
    $databaseWasSet = $true
    continue
  }

  $parts.Add($part.Trim())
}

if (-not $databaseWasSet) {
  $parts.Add("Database=$env:ADMIN_DATABASE")
}

[string]::Join(";", $parts)
')"

  if [[ -n "$connection_string" ]]; then
    export BODYLIFE_TEST_POSTGRES_ADMIN_CONNECTION_STRING="$connection_string"
    printf 'Using local PostgreSQL test admin connection from appsettings.Development.json ConnectionStrings:BodyLifeTestAdmin with Database=%s.\n' "$admin_database"
  fi
}

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
"$DOTNET_BIN" test "$ROOT_DIR/tests/BodyLife.Crm.Web.Tests/BodyLife.Crm.Web.Tests.csproj" \
  --configuration "$CONFIGURATION" \
  --no-build \
  --no-restore \
  --nologo
configure_local_postgres_test_connection
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
