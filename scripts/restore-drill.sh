#!/usr/bin/env bash
# Backup/restore drill for the PostgreSQL database (P1.7, SEC-05).
#
# Takes a logical backup of a source database with pg_dump, restores it into a new scratch database,
# and verifies the restored copy: same migration history, same tables, same row count in every table.
# The source database is only read. The scratch database is dropped at the end unless KEEP=1.
#
# This proves the dump/restore procedure and the schema's restorability. It is not evidence for a
# hosted provider's own backups (point-in-time restore): run the provider's restore as well and then
# point this script's verification at both databases (see docs/operations/runbook.md).
#
# Connection settings use the standard libpq environment variables, so no password is ever passed on
# the command line or written to a file by this script:
#   PGHOST PGPORT PGUSER PGPASSWORD   server and credentials (the role needs CREATEDB)
#   SOURCE_DB                         database to back up (required)
#   SCRATCH_DB                        database to create (default: <SOURCE_DB>_restore_drill)
#   KEEP=1                            keep the scratch database and dump file for inspection
set -euo pipefail

: "${SOURCE_DB:?Set SOURCE_DB to the database to back up}"
SCRATCH_DB="${SCRATCH_DB:-${SOURCE_DB}_restore_drill}"
if [[ ! "$SOURCE_DB" =~ ^[A-Za-z0-9_]+$ || ! "$SCRATCH_DB" =~ ^[A-Za-z0-9_]+$ ]]; then
  echo "Database names may contain only letters, digits and underscores." >&2
  exit 2
fi
if [[ "$SOURCE_DB" == "$SCRATCH_DB" ]]; then
  echo "SCRATCH_DB must differ from SOURCE_DB." >&2
  exit 2
fi

workdir="$(mktemp -d)"
dump="$workdir/backup.dump"
cleanup() {
  if [[ "${KEEP:-0}" != "1" ]]; then
    psql -d postgres -q -v ON_ERROR_STOP=1 -c "DROP DATABASE IF EXISTS \"$SCRATCH_DB\" WITH (FORCE)" >/dev/null 2>&1 || true
    rm -rf "$workdir"
  else
    echo "Kept scratch database $SCRATCH_DB and dump $dump"
  fi
}
trap cleanup EXIT

counts() {
  # One line per table: name and exact row count, in a stable order.
  psql -d "$1" -At -v ON_ERROR_STOP=1 -c \
    "SELECT format('SELECT %L || '' '' || count(*) FROM %I.%I', table_name, table_schema, table_name)
       FROM information_schema.tables
      WHERE table_schema = 'public' AND table_type = 'BASE TABLE'
      ORDER BY table_name" \
  | while IFS= read -r query; do psql -d "$1" -At -v ON_ERROR_STOP=1 -c "$query"; done
}

started=$(date +%s)
echo "[1/4] Backing up $SOURCE_DB"
pg_dump -d "$SOURCE_DB" --format=custom --no-owner --no-privileges --file "$dump"
backup_done=$(date +%s)
echo "      dump size: $(du -h "$dump" | cut -f1)"

echo "[2/4] Restoring into $SCRATCH_DB"
psql -d postgres -q -v ON_ERROR_STOP=1 -c "DROP DATABASE IF EXISTS \"$SCRATCH_DB\" WITH (FORCE)" -c "CREATE DATABASE \"$SCRATCH_DB\""
pg_restore -d "$SCRATCH_DB" --no-owner --no-privileges --exit-on-error "$dump"
restore_done=$(date +%s)

echo "[3/4] Comparing migration history"
migrations='SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1'
if ! diff <(psql -d "$SOURCE_DB" -At -c "$migrations") <(psql -d "$SCRATCH_DB" -At -c "$migrations") >/dev/null; then
  echo "FAILED: migration history differs between source and restored copy." >&2
  exit 1
fi
echo "      $(psql -d "$SCRATCH_DB" -At -c 'SELECT count(*) FROM "__EFMigrationsHistory"') migrations match"

echo "[4/4] Comparing row counts for every table"
counts "$SOURCE_DB" > "$workdir/source.txt"
counts "$SCRATCH_DB" > "$workdir/restored.txt"
if ! diff "$workdir/source.txt" "$workdir/restored.txt"; then
  echo "FAILED: row counts differ (source on the left). If the source was in use during the drill, repeat it while idle." >&2
  exit 1
fi
tables=$(wc -l < "$workdir/restored.txt" | tr -d ' ')
rows=$(awk '{ total += $NF } END { print total + 0 }' "$workdir/restored.txt")

echo "PASSED: $tables tables and $rows rows match."
echo "Backup took $((backup_done - started)) s, restore took $((restore_done - backup_done)) s. Record the date, these times and who ran the drill."
