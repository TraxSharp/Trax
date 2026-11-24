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

say "a release of new images leaves Postgres running"
sleep 2
out=$("${TRAX[@]}" deploy --env home --file "$DIR/trax.deploy.json" 2>&1) || { echo "$out"; fail "the second deploy failed"; }
echo "$out" | tail -n 6
echo "$out" | grep -q "Unchanged, left running: $STACK-database" || fail "a new image release restarts Postgres"
cp "$DIR/.trax/deploy/podman/$STACK"/*.kube "$UNITS/"
systemctl --user daemon-reload
eval "$(echo "$out" | grep -E '^\s+systemctl --user restart')"
for _ in $(seq 60); do curl -fsS "http://127.0.0.1:$port/trax/health" >/dev/null 2>&1 && break; sleep 2; done
curl -fsS "http://127.0.0.1:$port/trax/health" | grep -q Healthy || fail "the hub is not healthy after the release"
[ "$(podman inspect --format '{{.State.StartedAt}}' "$STACK-database-pod-database")" = "$database_started" ] ||
  fail "Postgres restarted during a release that did not change it"

say "podman: every check passed"
