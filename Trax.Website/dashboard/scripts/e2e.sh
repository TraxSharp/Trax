#!/usr/bin/env bash
# Full write-path e2e: (re)create a disposable DB, launch a dedicated devhost against it, seed
# known rows, run the e2e vitest suite (reads + schema drift + real mutations), tear down.
#
#   npm run test:e2e
#
# Requires: a Postgres container (TRAX_E2E_CONTAINER, default trax_stress_db) and a built
# dashboard-devhost. TRAX_TEST_PG_PORT moves the Postgres port off 5432, as in the .NET suites:
#
#   TRAX_E2E_CONTAINER=trax_database TRAX_TEST_PG_PORT=5433 npm run test:e2e
set -uo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
DASH="$(cd "$HERE/.." && pwd)"
DEVHOST="$(cd "$DASH/../dashboard-devhost" && pwd)"

DB="${TRAX_E2E_DB:-trax_dashboard_e2e}"
PORT="${TRAX_E2E_PORT:-5311}"
CONTAINER="${TRAX_E2E_CONTAINER:-trax_stress_db}"
KEY="admin-key-do-not-use-in-production"
URL="http://localhost:$PORT/trax/graphql"
PG_PORT="${TRAX_TEST_PG_PORT:-5432}"
CONN="Host=localhost;Port=$PG_PORT;Database=$DB;Username=trax;Password=trax123"
LOG="/tmp/trax-e2e-devhost.log"

kill_devhost() { lsof -ti tcp:"$PORT" 2>/dev/null | xargs kill 2>/dev/null || true; }
cleanup() { kill_devhost; }
trap cleanup EXIT

echo "==> stop any stale devhost on :$PORT"
kill_devhost
sleep 1

echo "==> (re)create database $DB"
docker exec "$CONTAINER" psql -U trax -d postgres -tAc \
  "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname='$DB' AND pid <> pg_backend_pid();" >/dev/null 2>&1
docker exec "$CONTAINER" psql -U trax -d postgres -c "DROP DATABASE IF EXISTS $DB;" >/dev/null
docker exec "$CONTAINER" psql -U trax -d postgres -c "CREATE DATABASE $DB;" >/dev/null

echo "==> build devhost"
# The devhost pins the local feed's 1.99.99, a version that never changes, so a plain restore is a
# no-op that keeps the dependency graph of whatever was packed first. Force it to re-read the feed.
( cd "$DEVHOST" && dotnet restore --force-evaluate && dotnet build -c Debug --no-restore ) >/dev/null 2>&1 || { echo "devhost build failed"; exit 1; }

echo "==> launch e2e devhost on :$PORT -> $DB"
( cd "$DEVHOST" && ASPNETCORE_ENVIRONMENT=Development TRAX_DEMO_CONN="$CONN" ASPNETCORE_URLS="http://localhost:$PORT" \
    dotnet run -c Debug --no-build ) >"$LOG" 2>&1 &

echo "==> wait for schema (first health query migrates the DB)"
up=0
for _ in $(seq 1 60); do
  if curl -s "$URL" -X POST -H "Content-Type: application/json" -H "X-Api-Key: $KEY" \
       -d '{"query":"{ operations { health { status } } }"}' 2>/dev/null | grep -q '"status"'; then
    up=1; break
  fi
  sleep 1
done
if [ "$up" != "1" ]; then echo "devhost did not come up; see $LOG"; tail -20 "$LOG"; exit 1; fi

echo "==> seed"
docker exec -i "$CONTAINER" psql -U trax -d "$DB" -v ON_ERROR_STOP=1 < "$HERE/e2e-seed.sql" >/dev/null || {
  echo "seed failed"; exit 1;
}

echo "==> run e2e tests"
( cd "$DASH" && TRAX_E2E_URL="$URL" npx vitest run --config vitest.e2e.config.ts )
