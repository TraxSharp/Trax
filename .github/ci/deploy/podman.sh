#!/usr/bin/env bash
# Deploys the DistributedWorkers sample's home environment (Trax.Samples/samples/DistributedWorkers/trax.deploy.json, a
# kubernetes environment with "runtime": "podman": the EnergyHub hub, a worker, Postgres and RabbitMQ) on this machine
# the way its output says to: the Quadlet units under systemd --user. Then releases again with only the images
# changed and checks Postgres and RabbitMQ kept running. Needs rootless podman 5 with Quadlet, a systemd user session
# (loginctl enable-linger), and the .NET SDK. Run from the repository root.
set -euo pipefail

DIR=Trax.Samples/samples/DistributedWorkers
APP=energyhub
STACK=$APP-home
FILE=$DIR/trax.deploy.podman.json
UNITS=$HOME/.config/containers/systemd
TRAX=(dotnet run --project Trax.Cli/src/Trax.Cli/Trax.Cli.csproj -c Release --no-launch-profile --)
export XDG_RUNTIME_DIR=${XDG_RUNTIME_DIR:-/run/user/$(id -u)}
# podman exec joins the container's cgroup through the systemd user manager, which it finds on this bus. A shell with
# no login session (ssh with a command, setsid) has neither variable set.
export DBUS_SESSION_BUS_ADDRESS=${DBUS_SESSION_BUS_ADDRESS:-unix:path=$XDG_RUNTIME_DIR/bus}

say() { printf '\n== %s\n' "$*"; }
fail() { printf 'FAIL: %s\n' "$*" >&2; exit 1; }

# The .env beside the file holds the stack's secret values. One that is already there is someone's own; this script
# writes and deletes its own, so it stops rather than overwrite it.
[ -e "$DIR/.env" ] && { echo "FAIL: $DIR/.env exists; move it aside first" >&2; exit 1; }

cleanup() {
  status=$?
  if [ "$status" -ne 0 ]; then
    say "state at failure"
    systemctl --user --no-pager status "$STACK-*" 2>/dev/null | tail -n 60 || true
    # The own journal of each unit that failed or was restarted, start to end: the tail of the shared journal shows
    # the retry, not the first failure.
    for unit in $(systemctl --user --no-pager --plain --no-legend list-units --all "$STACK-*.service" 2>/dev/null | awk '{print $1}'); do
      [ "$(systemctl --user show -p NRestarts --value "$unit" 2>/dev/null)" != 0 ] \
        || [ "$(systemctl --user show -p Result --value "$unit" 2>/dev/null)" != success ] || continue
      say "journal of $unit"
      journalctl --user --no-pager -o short-precise -u "$unit" 2>/dev/null | grep -v 'Copying blob' | head -n 120 || true
    done
    journalctl --user --no-pager -n 80 2>/dev/null || true
  fi
  systemctl --user stop "$STACK-*.service" 2>/dev/null || true
  systemctl --user reset-failed "$STACK-*.service" 2>/dev/null || true
  rm -f "$UNITS/$STACK"*.kube "$UNITS/$STACK.network"
  systemctl --user daemon-reload 2>/dev/null || true
  podman pod rm -f -a --filter "name=$STACK" >/dev/null 2>&1 || true
  podman network rm -f "$STACK" >/dev/null 2>&1 || true
  [ -n "${install:-}" ] && kill "$install" 2>/dev/null || true
  podman volume rm -f "$STACK-data" >/dev/null 2>&1 || true
  rm -rf "$DIR/.trax" "$DIR/.env" "$FILE" "${XDG_CONFIG_HOME:-$HOME/.config}/trax/$STACK"
  exit "$status"
}
trap cleanup EXIT

# trax and the sample's services restore from their committed lockfiles, as in every other CI job, so a lockfile that
# drifted stops the run here rather than resolving afresh. The sample's library comes with the hub and the worker.
for project in Trax.Cli/src/Trax.Cli/Trax.Cli.csproj \
  "$DIR/Trax.Samples.EnergyHub.Hub/Trax.Samples.EnergyHub.Hub.csproj" \
  "$DIR/Trax.Samples.EnergyHub.Worker/Trax.Samples.EnergyHub.Worker.csproj"; do
  dotnet restore "$project" --locked-mode -v q
done

say "the .env the Secrets are filled from"
{
  printf 'POSTGRES_PASSWORD=%s\n' "$(openssl rand -hex 24)"
  printf 'RABBITMQ_PASSWORD=%s\n' "$(openssl rand -hex 24)"
  printf 'Trax__BroadcastSigningKey=%s\n' "$(openssl rand -base64 32)"
} > "$DIR/.env"
chmod 600 "$DIR/.env"
# The sample's file without its services' egress: a development build of trax names no egress image, and this
# script does not build one into podman. The cluster script proves the egress.
jq 'del(.services[].egress)' "$DIR/trax.deploy.json" > "$FILE"

say "first deploy writes the units"
out=$("${TRAX[@]}" deploy --env home --file "$FILE" 2>&1) || { echo "$out"; fail "the first deploy failed"; }
echo "$out" | tail -n 8
for unit in database rabbitmq hub worker; do
  echo "$out" | grep -Eq "systemctl --user restart .*$STACK-$unit.service" || fail "a first release does not start $unit"
done
grep -rq 'hostPort' "$DIR/.trax/deploy/podman/$STACK"/*.yaml && echo "(a hub port is published on 127.0.0.1)"

say "install and start them as printed"
mkdir -p "$UNITS"
cp "$DIR/.trax/deploy/podman/$STACK"/*.kube "$DIR/.trax/deploy/podman/$STACK"/*.network "$UNITS/"
systemctl --user daemon-reload
eval "$(echo "$out" | grep -E '^\s+systemctl --user restart')"

for _ in $(seq 120); do
  systemctl --user is-active --quiet "$STACK-hub.service" && systemctl --user is-active --quiet "$STACK-worker.service" &&
    break
  sleep 2
done
for unit in hub worker rabbitmq database; do
  systemctl --user is-active --quiet "$STACK-$unit.service" || fail "the $unit unit did not start"
done
port=$(grep -ho 'PublishPort=127.0.0.1:[0-9]*' "$UNITS/$STACK-hub.kube" | cut -d: -f2) ||
  fail "the hub unit publishes no port"
for _ in $(seq 60); do curl -fsS "http://127.0.0.1:$port/trax/health" >/dev/null 2>&1 && break; sleep 2; done
curl -fsS "http://127.0.0.1:$port/trax/health" | grep -q Healthy || fail "the hub is not healthy"
answer=$(curl -fsS -H 'Content-Type: application/json' \
  -d '{"query":"{ discover { solar { monitorSolarProduction(input: {arrayId: \"SPA-001\", region: \"somerset\"}) { arrayId } } } }"}' \
  "http://127.0.0.1:$port/trax/graphql")
echo "$answer" | grep -q '"arrayId":"SPA-001"' || fail "GraphQL did not answer: $answer"
echo "the hub answers on 127.0.0.1:$port"
database_started=$(podman inspect --format '{{.State.StartedAt}}' "$STACK-database-pod-database")
rabbitmq_started=$(podman inspect --format '{{.State.StartedAt}}' "$STACK-rabbitmq-pod-rabbitmq")

say "every Trax container is hardened under podman too"
for svc in hub worker; do
  [ "$(podman inspect --format '{{.HostConfig.ReadonlyRootfs}}' "$STACK-$svc-pod-$svc")" = true ] ||
    fail "the $svc's root is writable"
done

say "a worker runs the hub's schedule and reports through RabbitMQ"
psql() { podman exec "$STACK-database-pod-database" psql -U "$APP" -d "$APP" -tAq -c "$1"; }
for _ in $(seq 150); do
  run=$(psql "SELECT id FROM trax.metadata WHERE train_state = 'completed' AND name LIKE '%EnergyHub%' LIMIT 1")
  [ -n "$run" ] && break
  sleep 2
done
[ -n "$run" ] || fail "no sample train finished on the worker"
connections=$(podman exec "$STACK-rabbitmq-pod-rabbitmq" rabbitmqctl -q list_connections user | grep -c "^$APP$" || true)
[ "$connections" -ge 2 ] || fail "the hub and the worker are not both connected to RabbitMQ ($connections connections)"
echo "run $run finished on the worker; $connections connections to RabbitMQ"

say "a release of new images drains first, then leaves Postgres and RabbitMQ running"
# A run InProgress now, standing for a long one: the sample's runs end in about a second, so one is put back to
# InProgress here and ended by hand once the drain has held the restart for a while.
live=$(psql "UPDATE trax.metadata SET train_state = 'in_progress', end_time = NULL
  WHERE id = (SELECT max(id) FROM trax.metadata WHERE train_state = 'completed' AND name LIKE '%EnergyHub%')
  RETURNING id")
[ -n "$live" ] || fail "no finished sample run to stand in for a live one"
hub_started=$(podman inspect --format '{{.State.StartedAt}}' "$STACK-hub-pod-hub")
worker_started=$(podman inspect --format '{{.State.StartedAt}}' "$STACK-worker-pod-worker")
out=$("${TRAX[@]}" deploy --env home --file "$FILE" 2>&1) || { echo "$out"; fail "the second deploy failed"; }
echo "$out" | tail -n 8
kept=$(echo "$out" | grep 'Unchanged, left running:') || fail "a new image release leaves nothing running"
for unit in database rabbitmq; do
  echo "$kept" | grep -q "$STACK-$unit" || fail "a new image release restarts $unit"
done
echo "$out" | grep -q -- "-drain.yaml" || fail "a release that restarts the hub does not drain first"
cp "$DIR/.trax/deploy/podman/$STACK"/*.kube "$UNITS/"
systemctl --user daemon-reload
# As printed: the drain pod, the restart only once it exits 0, then the resume pod.
( eval "$(echo "$out" | grep -E '^\s+(systemctl --user restart|\S*podman kube play)')" ) > "$DIR/.trax/install.log" 2>&1 &
install=$!
for _ in $(seq 120); do
  podman logs "$STACK-drain-pod-drain" 2>/dev/null | grep -q 'still busy' && break
  sleep 1
done
podman logs "$STACK-drain-pod-drain" 2>&1 | grep 'still busy' | tail -n 2 || fail "the drain never reported the live run"
sleep 10
[ "$(podman inspect --format '{{.State.StartedAt}}' "$STACK-hub-pod-hub")" = "$hub_started" ] ||
  fail "the hub restarted while a run was still busy"
[ "$(podman inspect --format '{{.State.StartedAt}}' "$STACK-worker-pod-worker")" = "$worker_started" ] ||
  fail "the worker restarted while a run was still busy"
psql "UPDATE trax.metadata SET train_state = 'completed', end_time = now() WHERE id = $live" >/dev/null
wait "$install" || { cat "$DIR/.trax/install.log"; fail "the drained release did not install"; }
[ "$(podman inspect --format '{{.State.StartedAt}}' "$STACK-hub-pod-hub")" != "$hub_started" ] ||
  fail "the hub did not restart after the drain"
[ "$(podman inspect --format '{{.State.StartedAt}}' "$STACK-worker-pod-worker")" != "$worker_started" ] ||
  fail "the worker did not restart after the drain"
[ "$(psql "SELECT train_state FROM trax.metadata WHERE id = $live")" = completed ] ||
  fail "run $live did not end as it was ended: the restarted hub's recovery reached it"
[ -z "$(psql "SELECT 1 FROM trax.drain")" ] || fail "the drain was not ended after the release"
echo "the drain held the restart until run $live ended, then the hub and the worker restarted and the drain ended"
for _ in $(seq 60); do curl -fsS "http://127.0.0.1:$port/trax/health" >/dev/null 2>&1 && break; sleep 2; done
curl -fsS "http://127.0.0.1:$port/trax/health" | grep -q Healthy || fail "the hub is not healthy after the release"
[ "$(podman inspect --format '{{.State.StartedAt}}' "$STACK-database-pod-database")" = "$database_started" ] ||
  fail "Postgres restarted during a release that did not change it"
[ "$(podman inspect --format '{{.State.StartedAt}}' "$STACK-rabbitmq-pod-rabbitmq")" = "$rabbitmq_started" ] ||
  fail "RabbitMQ restarted during a release that did not change it"

say "podman: every check passed"
