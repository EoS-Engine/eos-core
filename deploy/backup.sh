#!/usr/bin/env bash
# WP-030-06: Infrastructure Roadmap Phase 8's daily backup procedure — a single filesystem-level
# tar.gz archive of EOS_DATA_DIR's {sql,redis,chroma} bind mounts plus config/ and .env, written
# to a caller-supplied destination, with 7-daily/4-weekly retention. Deliberately not a SQL-native
# backup (Phase 8's own "Common Mistakes": "over-engineering this into a database-specific
# export/import tool chain before a simple filesystem-level archive has been shown to be
# insufficient") and deliberately accepts live-backup risk rather than stopping containers
# (Phase 8: "a reasonable trade-off at this scale").
set -euo pipefail

log() {
    printf '[%s] %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$*"
}

if [ "$#" -ne 1 ]; then
    echo "Usage: $0 <destination-dir>" >&2
    exit 2
fi

DESTINATION="$1"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

if [ -z "${EOS_DATA_DIR:-}" ]; then
    echo "EOS_DATA_DIR is not set." >&2
    exit 3
fi

for required in "$EOS_DATA_DIR/sql" "$EOS_DATA_DIR/redis" "$EOS_DATA_DIR/chroma" "$REPO_ROOT/config" "$REPO_ROOT/.env"; do
    if [ ! -e "$required" ]; then
        echo "Required source path is missing: $required" >&2
        exit 3
    fi
done

mkdir -p "$DESTINATION"

TIMESTAMP="$(date -u +%Y%m%d-%H%M%S)"
ARCHIVE_PATH="$DESTINATION/eos-backup-$TIMESTAMP.tar.gz"
SUFFIX=1
while [ -e "$ARCHIVE_PATH" ]; do
    ARCHIVE_PATH="$DESTINATION/eos-backup-$TIMESTAMP-$SUFFIX.tar.gz"
    SUFFIX=$((SUFFIX + 1))
done

log "Starting backup -> $ARCHIVE_PATH"

# Multiple -C transitions normalize every member's archive-internal path (sql/, redis/, chroma/,
# config/, .env at the archive root) — the absolute EOS_DATA_DIR/REPO_ROOT host paths are never
# embedded in the archive. The subshell's restrictive umask ensures the archive is created at
# 600 from its very first write, closing the window between creation and the chmod below (which
# is kept as a defense-in-depth final assertion, not the sole guarantee).
( umask 077; tar czf "$ARCHIVE_PATH" \
    -C "$EOS_DATA_DIR" sql redis chroma \
    -C "$REPO_ROOT" config .env )

chmod 600 "$ARCHIVE_PATH"

log "Archive created: $ARCHIVE_PATH ($(du -h "$ARCHIVE_PATH" | cut -f1))"

# WP-030-06 approved retention algorithm (exact, not to be re-derived):
#   - keep every archive from the last 7 calendar days (age 0-6 days, today inclusive);
#   - for each of the 4 preceding non-overlapping 7-day buckets (8-14, 15-21, 22-28, 29-35 days
#     ago), keep only the single most-recent archive in that bucket;
#   - delete every other file matching eos-backup-*.tar.gz in the destination.
prune_retention() {
    local destination="$1"
    local today_epoch
    today_epoch="$(date -u -d "$(date -u +%Y-%m-%d)" +%s)"

    local -a candidates=()
    while IFS= read -r -d '' f; do
        candidates+=("$f")
    done < <(find "$destination" -maxdepth 1 -type f -name 'eos-backup-*.tar.gz' -print0)

    # Only files matching this script's own exact naming convention are ever considered for
    # retention — anything else (manual copies, unrelated files a foreign process dropped in the
    # destination) is excluded from "files" entirely here, up front, so neither the aging logic
    # nor the final deletion loop below can ever parse or delete it.
    local -a files=()
    local candidate
    for candidate in "${candidates[@]}"; do
        if [[ "$(basename "$candidate")" =~ ^eos-backup-[0-9]{8}-[0-9]{6}(-[0-9]+)?\.tar\.gz$ ]]; then
            files+=("$candidate")
        fi
    done

    local -A bucket_best_file=()
    local -A bucket_best_age=()
    local -a keep=()

    local f base date_part file_epoch age_days bucket
    for f in "${files[@]}"; do
        base="$(basename "$f")"
        date_part="$(printf '%s' "$base" | sed -E 's/^eos-backup-([0-9]{8})-[0-9]{6}.*\.tar\.gz$/\1/')"
        file_epoch="$(date -u -d "${date_part:0:4}-${date_part:4:2}-${date_part:6:2}" +%s)"
        age_days=$(( (today_epoch - file_epoch) / 86400 ))

        if [ "$age_days" -le 6 ]; then
            keep+=("$f")
            continue
        fi

        bucket=""
        if [ "$age_days" -ge 8 ] && [ "$age_days" -le 14 ]; then
            bucket="1"
        elif [ "$age_days" -ge 15 ] && [ "$age_days" -le 21 ]; then
            bucket="2"
        elif [ "$age_days" -ge 22 ] && [ "$age_days" -le 28 ]; then
            bucket="3"
        elif [ "$age_days" -ge 29 ] && [ "$age_days" -le 35 ]; then
            bucket="4"
        else
            continue
        fi

        if [ -z "${bucket_best_age[$bucket]:-}" ] || [ "$age_days" -lt "${bucket_best_age[$bucket]}" ]; then
            bucket_best_age["$bucket"]="$age_days"
            bucket_best_file["$bucket"]="$f"
        fi
    done

    for bucket in "${!bucket_best_file[@]}"; do
        keep+=("${bucket_best_file[$bucket]}")
    done

    for f in "${files[@]}"; do
        local should_keep=0
        local k
        for k in "${keep[@]}"; do
            if [ "$k" = "$f" ]; then
                should_keep=1
                break
            fi
        done
        if [ "$should_keep" -eq 0 ]; then
            log "Pruning expired archive: $f"
            rm -f -- "$f"
        fi
    done
}

prune_retention "$DESTINATION"

log "Backup complete."
exit 0
