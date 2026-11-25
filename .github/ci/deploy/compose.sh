#!/usr/bin/env bash
# Deploys the DistributedWorkers sample (Trax.Samples/samples/DistributedWorkers: the EnergyHub hub, its workers,
# Postgres and RabbitMQ) as a compose stack and runs the release exactly as `trax deploy` prints it. Then builds the
# images again, holds a run InProgress and runs the printed release once more: the drain must report the run busy and
# keep the old hub and workers running until it ends, then replace them and end the drain, with Postgres and RabbitMQ
# left running. ENGINE is the compose engine (docker by default, podman for podman compose). Needs that engine with
# compose, jq, openssl and the .NET SDK. Run from the repository root.
set -euo pipefail

ENGINE=${ENGINE:-docker}
DIR=Trax.Samples/samples/DistributedWorkers
APP=energyhub
FILE=$DIR/trax.deploy.compose.json
OUT=$DIR/.trax/deploy/compose
TRAX=(dotnet run --project Trax.Cli/src/Trax.Cli/Trax.Cli.csproj -c Release --no-launch-profile --)

say() { printf '\n== %s\n' "$*"; }
fail() { printf 'FAIL: %s\n' "$*" >&2; exit 1; }
compose() { (cd "$OUT" && "$ENGINE" compose "$@"); }
started() { "$ENGINE" inspect --format '{{.State.StartedAt}}' "$(compose ps -q "$1" | head -n 1)"; }
psql() { compose exec -T database psql -U "$APP" -d "$APP" -tAq -c "$1"; }

[ -e "$FILE" ] && fail "$FILE exists; move it aside first"

cleanup() {
  status=$?
  if [ "$status" -ne 0 ] && [ -f "$OUT/compose.yaml" ]; then
    say "state at failure"
    compose ps -a || true
    compose logs --tail 60 drain hub || true
  fi
  [ -n "${install:-}" ] && kill "$install" 2>/dev/null || true
  [ -f "$OUT/compose.yaml" ] && compose down -v --remove-orphans >/dev/null 2>&1 || true
  rm -rf "$DIR/.trax" "$FILE"
  exit "$status"
}
trap cleanup EXIT

# The sample's file with a compose environment for this engine, and without its services' egress: a development
# build of trax names no egress image. The cluster script proves the egress.
jq --arg engine "$ENGINE" 'del(.services[].egress)
  | .environments = { compose: { target: "compose", engine: $engine, drainTimeout: "00:05:00" } }' \
  "$DIR/trax.deploy.json" > "$FILE"

deploy() {
  "${TRAX[@]}" deploy --env compose --file "$FILE" --out "$OUT" --build 2>&1
}
# The release as printed: the line after "then in <dir> run:".
release_of() { echo "$1" | grep -E "^\s+$ENGINE compose up -d migrate drain && " | sed 's/^\s*//'; }

say "first deploy writes the stack and builds the images"
out=$(deploy) || { echo "$out"; fail "the first deploy failed"; }
echo "$out" | tail -n 4
release=$(release_of "$out")
[ -n "$release" ] || fail "the deploy printed no release"
{
  sed -n 's/^\([A-Za-z_][A-Za-z0-9_]*\)=$/\1/p' "$OUT/.env.example" | while read -r key; do
    case $key in
      Broadcast__AllowInsecureTransport) printf '%s=true\n' "$key" ;;
      Trax__BroadcastSigningKey) printf '%s=%s\n' "$key" "$(openssl rand -base64 32)" ;;
      *) printf '%s=%s\n' "$key" "$(openssl rand -hex 24)" ;;
    esac
  done
  echo "TRAX_DRAIN_FORCE=false"
  echo "TRAX_IMAGE_TAG=latest"
} > "$OUT/.env"
chmod 600 "$OUT/.env"

say "run the release as printed"
(cd "$OUT" && eval "$release") || fail "the first release failed"
for _ in $(seq 120); do
  [ "$(compose ps --status running -q hub | wc -l)" -ge 1 ] && break
  sleep 2
done
port=$(grep -o '127\.0\.0\.1:[0-9]*:8080' "$OUT/compose.yaml" | head -n 1 | cut -d: -f2)
[ -n "$port" ] || fail "the hub publishes no port"
for _ in $(seq 60); do curl -fsS "http://127.0.0.1:$port/trax/health" >/dev/null 2>&1 && break; sleep 2; done
curl -fsS "http://127.0.0.1:$port/trax/health" | grep -q Healthy || fail "the hub is not healthy"

say "a worker runs the hub's schedule"
for _ in $(seq 150); do
  run=$(psql "SELECT id FROM trax.metadata WHERE train_state = 'completed' AND name LIKE '%EnergyHub%' LIMIT 1")
  [ -n "$run" ] && break
  sleep 2
done
[ -n "$run" ] || fail "no sample train finished on a worker"
database_started=$(started database)
rabbitmq_started=$(started rabbitmq)

say "a release of new images drains first, then leaves Postgres and RabbitMQ running"
out=$(deploy) || { echo "$out"; fail "the second deploy failed"; }
release=$(release_of "$out")
# A run InProgress now, standing for a long one: the sample's runs end in about a second, so one is put back to
# InProgress here and ended by hand once the drain has held the release for a while.
live=$(psql "UPDATE trax.metadata SET train_state = 'in_progress', end_time = NULL
  WHERE id = (SELECT max(id) FROM trax.metadata WHERE train_state = 'completed' AND name LIKE '%EnergyHub%')
  RETURNING id")
[ -n "$live" ] || fail "no finished sample run to stand in for a live one"
hub_started=$(started hub)
worker_started=$(started worker)
( cd "$OUT" && eval "$release" ) > "$DIR/.trax/install.log" 2>&1 &
install=$!
for _ in $(seq 120); do
  compose logs drain 2>/dev/null | grep -q 'still busy' && break
  sleep 1
done
compose logs drain 2>&1 | grep 'still busy' | tail -n 2 || fail "the drain never reported the live run"
sleep 10
[ "$(started hub)" = "$hub_started" ] || fail "the hub was replaced while a run was still busy"
[ "$(started worker)" = "$worker_started" ] || fail "a worker was replaced while a run was still busy"
psql "UPDATE trax.metadata SET train_state = 'completed', end_time = now() WHERE id = $live" >/dev/null
wait "$install" || { cat "$DIR/.trax/install.log"; fail "the drained release did not run"; }
install=
[ "$(started hub)" != "$hub_started" ] || fail "the hub was not replaced after the drain"
[ "$(psql "SELECT train_state FROM trax.metadata WHERE id = $live")" = completed ] ||
  fail "run $live did not end as it was ended: the new hub's recovery reached it"
for _ in $(seq 60); do [ -z "$(psql "SELECT 1 FROM trax.drain")" ] && break; sleep 2; done
[ -z "$(psql "SELECT 1 FROM trax.drain")" ] || fail "the drain was not ended after the release"
for _ in $(seq 60); do curl -fsS "http://127.0.0.1:$port/trax/health" >/dev/null 2>&1 && break; sleep 2; done
curl -fsS "http://127.0.0.1:$port/trax/health" | grep -q Healthy || fail "the hub is not healthy after the release"
[ "$(started database)" = "$database_started" ] || fail "Postgres was replaced during a release that did not change it"
[ "$(started rabbitmq)" = "$rabbitmq_started" ] || fail "RabbitMQ was replaced during a release that did not change it"
echo "the drain held the release until run $live ended, then the hub and workers were replaced and the drain ended"

say "compose: every check passed"
