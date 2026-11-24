#!/bin/sh
# Runs the egress image in real network namespaces under rootless podman and proves what it holds, the way a
# deployment uses it:
#
#   1. a pod as Kubernetes runs it: `rules` as root with NET_ADMIN once, then `serve` as the egress uid, then the
#      check as an app uid with no capabilities;
#   2. a pod as compose runs it: `run` (rules, then root and every capability given up, then serve);
#   3. the same pod with its sidecar stopped: nothing leaves at all (the rules fail closed).
#
# Each pod sits on a network of its own with a "database" (listed, must open) and an "other" service on the same
# port (not listed, must stay blocked), so the in-cluster allowance and the LAN refusal are both exercised.
#
# Usage, from Trax.Egress: test/e2e.sh <image> [allowed-domain]
# The allowed domain must be reachable from this machine over HTTPS (api.github.com by default). Everything it
# creates is named trax-egress-e2e-<pid> and removed on exit.
#
# The pods resolve through the network's own DNS (aardvark), as a deployment's do. On a host where aardvark does not
# answer on new networks, E2E_DNS=<resolver> gives the pods that upstream instead and lists the database by its
# address (tcp:<address>:5432), since no DNS there knows its name.
set -eu

image=${1:?usage: test/e2e.sh <image> [allowed-domain]}
domain=${2:-api.github.com}
name="trax-egress-e2e-$$"
app_uid=1654
fail=0

cleanup() {
  podman pod rm -f -t 0 "$name-k8s" "$name-compose" >/dev/null 2>&1 || true
  podman rm -f -t 0 "$name-database" "$name-other" >/dev/null 2>&1 || true
  podman network rm -f "$name" >/dev/null 2>&1 || true
}
trap cleanup EXIT INT TERM

say() { printf '\n== %s\n' "$*"; }

podman network create "$name" >/dev/null
# A listener that holds each connection open without answering, as Postgres does before the client speaks.
listener() {
  podman run -d --name "$name-$1" --network "$name" --network-alias "$1" --entrypoint sh "$image" \
    -c 'exec nc -lk -p 5432 -e cat' >/dev/null
}
listener database
listener other
other_ip=$(podman inspect -f '{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}' "$name-other")
gateway=$(podman network inspect -f '{{range .Subnets}}{{.Gateway}}{{end}}' "$name")
allow="$domain tcp:database:5432"
pod_dns=""
if [ -n "${E2E_DNS:-}" ]; then
  database_ip=$(podman inspect -f '{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}' "$name-database")
  pod_dns="--dns $E2E_DNS"
  allow="$domain tcp:$database_ip:5432"
fi
deny="$other_ip:5432 $gateway:80"

wait_ready() {
  i=0
  until podman exec "$1" trax-egress ready >/dev/null 2>&1; do
    i=$((i + 1))
    if [ "$i" -gt 30 ]; then
      echo "!! $1 never became ready"
      podman logs "$1" || true
      exit 1
    fi
    sleep 1
  done
}

check() {
  if podman run --rm --pod "$1" --user "$app_uid:$app_uid" --cap-drop ALL --security-opt no-new-privileges \
    -e TRAX_EGRESS_ALLOW="$allow" -e TRAX_EGRESS_CHECK_DENY="$deny" "$image" check; then
    echo "-> $1: as intended"
  else
    echo "!! $1: the check failed; the sidecar's log:"
    podman logs --tail 40 "$1-egress" 2>&1 | grep -v '^[[:space:]]' || true
    fail=1
  fi
}

say "kubernetes shape: rules, then serve, then the check as the app"
# shellcheck disable=SC2086 # pod_dns is a list of options
podman pod create --name "$name-k8s" --network "$name" --userns=nomap $pod_dns >/dev/null
podman run --rm --pod "$name-k8s" --user 0:0 --cap-drop ALL --cap-add NET_ADMIN --security-opt no-new-privileges \
  -e TRAX_EGRESS_ALLOW="$allow" "$image" rules
podman run -d --pod "$name-k8s" --name "$name-k8s-egress" --cap-drop ALL --security-opt no-new-privileges \
  --read-only -e TRAX_EGRESS_ALLOW="$allow" "$image" serve >/dev/null
wait_ready "$name-k8s-egress"
check "$name-k8s"

say "the check refuses to run as the egress's own uid"
if podman run --rm --pod "$name-k8s" --user 61000:61000 --cap-drop ALL -e TRAX_EGRESS_ALLOW="$allow" "$image" check \
  >/dev/null 2>&1; then
  echo "!! the check ran as the egress uid"
  fail=1
else
  echo "-> refused"
fi

say "the sidecar refuses to serve as any uid but its own"
if podman run --rm --pod "$name-k8s" --user "$app_uid:$app_uid" --cap-drop ALL "$image" serve >/dev/null 2>&1; then
  echo "!! serve ran as the app uid"
  fail=1
else
  echo "-> refused"
fi

say "fail closed: the sidecar stopped, nothing leaves"
podman stop -t 2 "$name-k8s-egress" >/dev/null
closed=$(podman run --rm --pod "$name-k8s" --user "$app_uid:$app_uid" --cap-drop ALL --entrypoint sh "$image" -c "
  out=''
  wget -q -T 5 -O /dev/null https://$domain/ 2>/dev/null && out=\"\$out https\"
  nslookup $domain >/dev/null 2>&1 && out=\"\$out dns\"
  nc -z -w 3 database 5432 2>/dev/null && out=\"\$out database\"
  nc -z -w 3 1.1.1.1 443 2>/dev/null && out=\"\$out direct\"
  echo \"\$out\"")
if [ -n "$closed" ]; then
  echo "!! with the sidecar stopped these still worked:$closed"
  fail=1
else
  echo "-> nothing left the pod"
fi

say "compose shape: run applies the rules, gives up root, and serves"
# shellcheck disable=SC2086 # pod_dns is a list of options
podman pod create --name "$name-compose" --network "$name" --userns=nomap $pod_dns >/dev/null
podman run -d --pod "$name-compose" --name "$name-compose-egress" --user 0:0 --cap-drop ALL \
  --cap-add NET_ADMIN --cap-add SETUID --cap-add SETGID --security-opt no-new-privileges --read-only \
  -e TRAX_EGRESS_ALLOW="$allow" "$image" run >/dev/null
wait_ready "$name-compose-egress"
status=$(podman exec "$name-compose-egress" sh -c 'grep -E "^(Uid|CapEff|CapPrm)" /proc/1/status')
echo "$status"
if echo "$status" | grep -q '^Uid:[[:space:]]*61000' && ! echo "$status" | grep -E '^Cap(Eff|Prm):' | grep -qv '0000000000000000'; then
  echo "-> the sidecar holds no root and no capability"
else
  echo "!! the sidecar kept root or a capability"
  fail=1
fi
check "$name-compose"

say "an invalid list stops the rules step, so the pod never starts"
if podman run --rm --pod "$name-compose" --user 0:0 --cap-drop ALL --cap-add NET_ADMIN \
  -e TRAX_EGRESS_ALLOW="*.github.com" "$image" rules >/dev/null 2>&1; then
  echo "!! a wildcard was accepted"
  fail=1
else
  echo "-> refused"
fi

if [ "$fail" -ne 0 ]; then
  echo
  echo "!! the egress is not as intended"
  exit 1
fi
echo
echo "egress as intended"
