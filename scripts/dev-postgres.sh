#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SERVICE_NAME="postgres"
DB_NAME="${BODYLIFE_POSTGRES_DB:-bodylife_crm_dev}"
DB_USER="${BODYLIFE_POSTGRES_USER:-bodylife}"
WAIT_TIMEOUT_SECONDS="${BODYLIFE_POSTGRES_WAIT_TIMEOUT_SECONDS:-60}"

usage() {
  cat <<'USAGE'
Usage: scripts/dev-postgres.sh <command>

Commands:
  up       Start local PostgreSQL and wait until it accepts connections.
  wait     Wait for the local PostgreSQL container to become ready.
  status   Show local PostgreSQL container status.
  logs     Follow local PostgreSQL logs.
  down     Stop local PostgreSQL without deleting the data volume.
  reset    Stop local PostgreSQL, delete its data volume, start it again, and wait.

Environment overrides:
  BODYLIFE_POSTGRES_PORT
  BODYLIFE_POSTGRES_DB
  BODYLIFE_POSTGRES_USER
  BODYLIFE_POSTGRES_PASSWORD
  BODYLIFE_POSTGRES_WAIT_TIMEOUT_SECONDS
USAGE
}

compose() {
  docker compose -f "$ROOT_DIR/docker-compose.yml" "$@"
}

wait_for_postgres() {
  local deadline=$((SECONDS + WAIT_TIMEOUT_SECONDS))

  printf 'Waiting for local PostgreSQL (%s/%s)...\n' "$DB_USER" "$DB_NAME"
  until compose exec -T "$SERVICE_NAME" pg_isready -U "$DB_USER" -d "$DB_NAME" >/dev/null 2>&1; do
    if (( SECONDS >= deadline )); then
      printf 'PostgreSQL did not become ready within %s seconds.\n' "$WAIT_TIMEOUT_SECONDS" >&2
      compose ps "$SERVICE_NAME" >&2 || true
      return 1
    fi

    sleep 1
  done

  printf 'Local PostgreSQL is ready on localhost:%s.\n' "${BODYLIFE_POSTGRES_PORT:-55432}"
}

command="${1:-}"
case "$command" in
  up)
    compose up -d "$SERVICE_NAME"
    wait_for_postgres
    ;;
  wait)
    wait_for_postgres
    ;;
  status)
    compose ps "$SERVICE_NAME"
    ;;
  logs)
    compose logs -f "$SERVICE_NAME"
    ;;
  down)
    compose down
    ;;
  reset)
    compose down -v
    compose up -d "$SERVICE_NAME"
    wait_for_postgres
    ;;
  -h|--help|help)
    usage
    ;;
  *)
    usage >&2
    exit 2
    ;;
esac
