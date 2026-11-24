#!/usr/bin/env bash
# Deploys .github/ci/deploy/trax.deploy.json's home environment (a kubernetes environment with "runtime": "podman")
# on this machine the way its output says to: the Quadlet units under systemd --user. Then releases again with only
# the images changed and checks Postgres kept running. Needs rootless podman 5 with Quadlet, a systemd user session
# (loginctl enable-linger), and the .NET SDK. Run from the repository root.
set -euo pipefail

DIR=.github/ci/deploy
STACK=traxci-home
UNITS=$HOME/.config/containers/systemd
TRAX=(dotnet run --project Trax.Cli/src/Trax.Cli/Trax.Cli.csproj -c Release --no-launch-profile --)
export XDG_RUNTIME_DIR=${XDG_RUNTIME_DIR:-/run/user/$(id -u)}

say() { printf '\n== %s\n' "$*"; }
fail() { printf 'FAIL: %s\n' "$*" >&2; exit 1; }

cleanup() {
  status=$?
  if [ "$status" -ne 0 ]; then
    say "state at failure"
    systemctl --user --no-pager status "$STACK-*" 2>/dev/null | tail -n 60 || true
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
  rm -rf "$DIR/.trax" "$DIR/.env" "${XDG_CONFIG_HOME:-$HOME/.config}/trax/$STACK"
  exit "$status"
}
trap cleanup EXIT

say "the .env the Secrets are filled from"
printf 'POSTGRES_PASSWORD=%s\n' "$(openssl rand -hex 24)" > "$DIR/.env"
chmod 600 "$DIR/.env"

say "first deploy writes the units"
out=$("${TRAX[@]}" deploy --env home --file "$DIR/trax.deploy.json" 2>&1) || { echo "$out"; fail "the first deploy failed"; }
echo "$out" | tail -n 8
echo "$out" | grep -q "systemctl --user restart $STACK-database.service" || fail "a first release does not start Postgres"
grep -rq 'hostPort' "$DIR/.trax/deploy/podman/$STACK"/*.yaml && echo "(a hub port is published on 127.0.0.1)"

say "install and start them as printed"
mkdir -p "$UNITS"
cp "$DIR/.trax/deploy/podman/$STACK"/*.kube "$DIR/.trax/deploy/podman/$STACK"/*.network "$UNITS/"
systemctl --user daemon-reload
eval "$(echo "$out" | grep -E '^\s+systemctl --user restart')"

for _ in $(seq 120); do
  systemctl --user is-active --quiet "$STACK-hub.service" && break
  sleep 2
done
systemctl --user is-active --quiet "$STACK-hub.service" || fail "the hub unit did not start"
port=$(grep -ho 'PublishPort=127.0.0.1:[0-9]*' "$UNITS/$STACK-hub.kube" | cut -d: -f2) ||
  fail "the hub unit publishes no port"
for _ in $(seq 60); do curl -fsS "http://127.0.0.1:$port/trax/health" >/dev/null 2>&1 && break; sleep 2; done
curl -fsS "http://127.0.0.1:$port/trax/health" | grep -q Healthy || fail "the hub is not healthy"
database_started=$(podman inspect --format '{{.State.StartedAt}}' "$STACK-database-pod-database")

say "every Trax container is hardened under podman too"
[ "$(podman inspect --format '{{.HostConfig.ReadonlyRootfs}}' "$STACK-hub-pod-hub")" = true ] ||
  fail "the hub's root is writable"

say "a release of new images drains first, then leaves Postgres running"
psql() { podman exec "$STACK-database-pod-database" psql -U traxci -d traxci -tAq -c "$1"; }
# A run InProgress on the hub serving now, standing for a long one: the hub's own runs end in moments, so one is put
# back to InProgress here and ended by hand once the drain has held the restart for a while.
for _ in $(seq 30); do
  live=$(psql "UPDATE trax.metadata SET train_state = 'in_progress', end_time = NULL
    WHERE id = (SELECT max(id) FROM trax.metadata WHERE train_state = 'completed' AND name LIKE '%HelloWorld%')
    RETURNING id")
  [ -n "$live" ] && break
  sleep 2
done
[ -n "$live" ] || fail "no finished hello-world run to stand in for a live one"
hub_started=$(podman inspect --format '{{.State.StartedAt}}' "$STACK-hub-pod-hub")
out=$("${TRAX[@]}" deploy --env home --file "$DIR/trax.deploy.json" 2>&1) || { echo "$out"; fail "the second deploy failed"; }
echo "$out" | tail -n 8
echo "$out" | grep -q "Unchanged, left running: $STACK-database" || fail "a new image release restarts Postgres"
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
podman logs "$STACK-drain-pod-drain" 2>&1 | grep -q 'still busy' || fail "the drain never reported the live run"
sleep 10
[ "$(podman inspect --format '{{.State.StartedAt}}' "$STACK-hub-pod-hub")" = "$hub_started" ] ||
  fail "the hub restarted while a run was still busy"
psql "UPDATE trax.metadata SET train_state = 'completed', end_time = now() WHERE id = $live" >/dev/null
wait "$install" || { cat "$DIR/.trax/install.log"; fail "the drained release did not install"; }
[ "$(podman inspect --format '{{.State.StartedAt}}' "$STACK-hub-pod-hub")" != "$hub_started" ] ||
  fail "the hub did not restart after the drain"
[ "$(psql "SELECT train_state FROM trax.metadata WHERE id = $live")" = completed ] ||
  fail "run $live did not end as it was ended: the restarted hub's recovery reached it"
[ -z "$(psql "SELECT 1 FROM trax.drain")" ] || fail "the drain was not ended after the release"
echo "the drain held the restart until run $live ended, then the hub restarted and the drain ended"
for _ in $(seq 60); do curl -fsS "http://127.0.0.1:$port/trax/health" >/dev/null 2>&1 && break; sleep 2; done
curl -fsS "http://127.0.0.1:$port/trax/health" | grep -q Healthy || fail "the hub is not healthy after the release"
[ "$(podman inspect --format '{{.State.StartedAt}}' "$STACK-database-pod-database")" = "$database_started" ] ||
  fail "Postgres restarted during a release that did not change it"

say "podman: every check passed"
