#!/usr/bin/env bash
# WP-030-07: restores a WP030-06 backup archive into an isolated drill root, starts an isolated
# (distinct container names/ports) Docker Compose stack from the restored data, and runs the
# existing, unmodified BootstrapRunner (via the dedicated EOS.RestoreDrill driver) against it.
# The live EOS stack is never touched, stopped, or depended upon.
set -euo pipefail

log() {
    printf '[%s] %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$*"
}

if [ "$#" -ne 2 ]; then
    echo "Usage: $0 <archive-path> <isolated-drill-root>" >&2
    exit 2
fi

ARCHIVE="$1"
DRILL_ROOT="$2"

if [ ! -f "$ARCHIVE" ] || [ ! -r "$ARCHIVE" ]; then
    echo "Archive is missing or unreadable: $ARCHIVE" >&2
    exit 3
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
COMPOSE_FILE="$SCRIPT_DIR/docker-compose.restore-drill.yml"
COMPOSE_PROJECT="eos-restore-drill"

COMPOSE_STARTED=0

cleanup() {
    if [ "$COMPOSE_STARTED" -eq 1 ]; then
        log "Tearing down isolated restore-drill stack."
        DRILL_ROOT="$DRILL_ROOT" DRILL_MSSQL_SA_PASSWORD="${DRILL_MSSQL_SA_PASSWORD:-unused}" \
            docker compose -f "$COMPOSE_FILE" -p "$COMPOSE_PROJECT" down --volumes >/dev/null 2>&1 || true
    fi

    if [ -d "$DRILL_ROOT" ]; then
        log "Cleaning up isolated drill root: $DRILL_ROOT"
        # SQL Server creates its own internal subdirectories/files at startup (e.g. .system)
        # owned by its own non-host container UID with permissions the host user cannot recurse
        # into — removing them as root from inside a throwaway container (reusing the same
        # already-pulled redis:7-alpine image, not a new dependency) is the standard, safe way to
        # clean up root/other-UID-owned bind-mount contents without sudo or new tooling.
        docker run --rm -v "$DRILL_ROOT:/drill" redis:7-alpine rm -rf -- /drill >/dev/null 2>&1 || true
        rm -rf -- "$DRILL_ROOT"
    fi
}
trap cleanup EXIT

mkdir -p "$DRILL_ROOT"

log "Extracting archive into isolated drill root: $DRILL_ROOT"
tar xzf "$ARCHIVE" -C "$DRILL_ROOT"

for required in sql redis chroma config .env; do
    if [ ! -e "$DRILL_ROOT/$required" ]; then
        echo "Extracted archive is missing required member: $required" >&2
        exit 3
    fi
done

# The isolated SQL Server/Redis containers run as their own internal, non-root, non-host UIDs
# (e.g. mssql's uid 10001) — tar extraction applies the invoking process's umask, which can strip
# the world-write bit even when the original archived directory had it, leaving the extracted
# copy unwritable by those container-internal users. This only touches the extracted copy inside
# the isolated drill root — never the archive itself, never any live data.
chmod -R o+rwX "$DRILL_ROOT/sql" "$DRILL_ROOT/redis" "$DRILL_ROOT/chroma"

# WP-030-07 critical safety rule: the archived config/Storage.json still says "~/eos/data" (the
# real, live directory). Rewriting ONLY this one extracted file — never the real repository's
# config/Storage.json — is what keeps the drill's SQLite writability probe (part of
# BootstrapRunner's existing "Start Infrastructure" step) isolated inside the drill root instead
# of touching the live environment.
DRILL_DATA_DIR="$DRILL_ROOT/data"
mkdir -p "$DRILL_DATA_DIR"
STORAGE_JSON="$DRILL_ROOT/config/Storage.json"
TMP_STORAGE_JSON="$(mktemp)"
printf '{\n  "dataDirectory": "%s"\n}\n' "$DRILL_DATA_DIR" > "$TMP_STORAGE_JSON"
mv "$TMP_STORAGE_JSON" "$STORAGE_JSON"

# Read only the SA password out of the extracted (never the real) .env, purely to build the
# isolated connection string below — never printed, never logged.
DRILL_MSSQL_SA_PASSWORD="$(grep -m1 '^MSSQL_SA_PASSWORD=' "$DRILL_ROOT/.env" | cut -d '=' -f2-)"
if [ -z "$DRILL_MSSQL_SA_PASSWORD" ]; then
    echo "Extracted .env does not contain MSSQL_SA_PASSWORD." >&2
    exit 3
fi

log "Starting isolated restore-drill Docker Compose stack."
DRILL_ROOT="$DRILL_ROOT" DRILL_MSSQL_SA_PASSWORD="$DRILL_MSSQL_SA_PASSWORD" \
    docker compose -f "$COMPOSE_FILE" -p "$COMPOSE_PROJECT" up -d --wait --wait-timeout 180
COMPOSE_STARTED=1

log "Isolated stack is healthy. Invoking the restore-drill driver against restored data."

export EOS_SQLSERVER_CONNECTION_STRING="Server=localhost,14330;Database=master;User Id=sa;Password=${DRILL_MSSQL_SA_PASSWORD};TrustServerCertificate=True"
export EOS_REDIS_CONNECTION_STRING="localhost:16380"
export EOS_CHROMADB_ENDPOINT="http://localhost:18001"

set +e
dotnet run --project "$REPO_ROOT/src/EOS.RestoreDrill/EOS.RestoreDrill.csproj" -- "$DRILL_ROOT"
DRIVER_EXIT_CODE=$?
set -e

unset EOS_SQLSERVER_CONNECTION_STRING EOS_REDIS_CONNECTION_STRING EOS_CHROMADB_ENDPOINT

if [ "$DRIVER_EXIT_CODE" -eq 0 ]; then
    log "Restore drill succeeded — BootstrapRunner reached Ready against restored data."
else
    log "Restore drill failed (driver exit code $DRIVER_EXIT_CODE)."
fi

exit "$DRIVER_EXIT_CODE"
