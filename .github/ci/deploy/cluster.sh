#!/usr/bin/env bash
# Deploys the DistributedWorkers sample (Trax.Samples/samples/DistributedWorkers/trax.deploy.json, its cluster
# environment: the EnergyHub hub, two workers, Postgres and RabbitMQ) to a real Kubernetes cluster and checks what it
# did. Needs kubectl, docker and the .NET SDK. Run from the repository root.
#
#   K8S=k3s       installs k3s on this machine (root, through sudo) with a registry on 127.0.0.1:5001 that it pulls
#                 from, and uninstalls both at the end. For a throwaway machine: the deploy-cluster CI job.
#   K8S=existing  (default) uses the current kubeconfig context and the registry in TRAX_CI_REGISTRY, such as k3s in
#                 a VM; it creates and deletes only its namespace (TRAX_CI_NAMESPACE, energyhub-cluster by default) and
#                 the egress admission policy and binding named after it.
#
# Not kind: a kind node is a container, so its pods cannot have a user namespace of their own, and every Trax pod
# asks for one (hostUsers: false). They never start there (proved 2026-11-24 on kind v0.33.0, node v1.36.4).
set -euo pipefail

K8S=${K8S:-existing}
K3S_VERSION=v1.36.6+k3s1
K3S_INSTALL_SHA256=46177d4c99440b4c0311b67233823a8e8a2fc09693f6c89af1a7161e152fbfad
REGISTRY_NAME=trax-deploy-registry
REGISTRY_IMAGE=docker.io/library/registry:3@sha256:82f6ff4a7f982960c8aa51617fea3aabc485df6e55cce80d22deb1de36f1bc3c
# The sample's own trax.deploy.json. The copies this script makes sit beside it, since its projects are named
# relative to it.
DIR=Trax.Samples/samples/DistributedWorkers
APP=energyhub
NS=${TRAX_CI_NAMESPACE:-energyhub-cluster}
# The app's Secret is named after the app and environment, wherever the namespace is.
SECRET=$APP-cluster
BUSYBOX=docker.io/library/busybox:1.37@sha256:a7dbc319b1438115a207e11930dc4bb3e643dcb3ec0bdd694bb36cd8fad289ec
# The local port the hub is forwarded to for the probes; another run on the same machine may hold 18080.
PORT=${TRAX_CI_PORT:-18080}
RUNNER_TEMP_DIR=${RUNNER_TEMP:-$(mktemp -d)}
TRAX=(dotnet run --project Trax.Cli/src/Trax.Cli/Trax.Cli.csproj -c Release --no-launch-profile --)

say() { printf '\n== %s\n' "$*"; }
fail() { printf 'FAIL: %s\n' "$*" >&2; exit 1; }

cleanup() {
  status=$?
  if [ "$status" -ne 0 ]; then
    say "state at failure"
    kubectl -n "$NS" get all,jobs,pvc -o wide 2>/dev/null || true
    kubectl -n "$NS" describe pods 2>/dev/null | tail -n 80 || true
    kubectl -n "$NS" logs -l app.kubernetes.io/part-of=$APP --all-containers --tail=50 --prefix 2>/dev/null || true
  fi
  [ -n "${forward:-}" ] && kill "$forward" 2>/dev/null || true
  [ -n "${watcher:-}" ] && kill "$watcher" 2>/dev/null || true
  [ -n "${deploy:-}" ] && kill "$deploy" 2>/dev/null || true
  kubectl delete namespace "$NS" --ignore-not-found --timeout=180s >/dev/null 2>&1 || true
  # Cluster-wide, so deleting the namespace leaves them.
  kubectl delete validatingadmissionpolicybinding,validatingadmissionpolicy "trax-egress-$NS" --ignore-not-found \
    >/dev/null 2>&1 || true
  rm -f "$DIR/trax.deploy.ci.json" "$DIR/trax.deploy.broken.json" "$DIR/trax.deploy.egress.json"
  if [ "$K8S" = k3s ]; then
    sudo /usr/local/bin/k3s-uninstall.sh >/dev/null 2>&1 || true
    docker rm -f "$REGISTRY_NAME" >/dev/null 2>&1 || true
  fi
  exit "$status"
}
trap cleanup EXIT

if [ "$K8S" = k3s ]; then
  say "k3s $K3S_VERSION and a registry it pulls from"
  docker run -d --restart=no --name "$REGISTRY_NAME" -p 127.0.0.1:5001:5000 "$REGISTRY_IMAGE" >/dev/null
  sudo mkdir -p /etc/rancher/k3s
  printf 'mirrors:\n  "localhost:5001":\n    endpoint: ["http://127.0.0.1:5001"]\n' |
    sudo tee /etc/rancher/k3s/registries.yaml >/dev/null
  # The installer at the release's tag, checked against the digest pinned here; it checks the k3s binary it
  # downloads against the release's own sums. Bump K3S_VERSION and the digest together.
  curl -sSfL -o "$RUNNER_TEMP_DIR/k3s-install.sh" \
    "https://raw.githubusercontent.com/k3s-io/k3s/${K3S_VERSION/+/%2B}/install.sh"
  echo "$K3S_INSTALL_SHA256  $RUNNER_TEMP_DIR/k3s-install.sh" | sha256sum -c -
  sudo INSTALL_K3S_VERSION="$K3S_VERSION" sh "$RUNNER_TEMP_DIR/k3s-install.sh" --disable traefik \
    --write-kubeconfig-mode 600 >/dev/null
  mkdir -p "$HOME/.kube"
  sudo cat /etc/rancher/k3s/k3s.yaml > "$HOME/.kube/config"
  chmod 600 "$HOME/.kube/config"
  # k3s's own kubectl reads /etc/rancher/k3s/k3s.yaml, which only root may read, unless told otherwise.
  export KUBECONFIG=$HOME/.kube/config
  # The node registers a few seconds after k3s starts; wait for it to exist, then to be ready.
  for _ in $(seq 60); do [ -n "$(kubectl get nodes -o name 2>/dev/null)" ] && break; sleep 2; done
  kubectl wait --for=condition=Ready node --all --timeout=120s
  registry=localhost:5001
else
  registry=${TRAX_CI_REGISTRY:?set TRAX_CI_REGISTRY to a registry the cluster pulls from}
fi
kubectl version >/dev/null

# The sample's file with this run's registry and namespace. Its services' egress is taken out here and put back for
# the last check: the egress image has to be built and named first, and a development build of trax has none.
jq --arg r "$registry" --arg ns "$NS" \
  '.environments.cluster.registry = $r | .environments.cluster.namespace = $ns | del(.services[].egress)' \
  "$DIR/trax.deploy.json" > "$DIR/trax.deploy.ci.json"

say "the namespace and its Secret, which trax never writes"
# A namespace an earlier run left is waited out, so this run starts from nothing.
kubectl delete namespace "$NS" --ignore-not-found --timeout=180s
kubectl create namespace "$NS"
# Postgres's and RabbitMQ's passwords, and the key the hub and the workers sign their events with.
{
  printf 'POSTGRES_PASSWORD=%s\n' "$(openssl rand -hex 24)"
  printf 'RABBITMQ_PASSWORD=%s\n' "$(openssl rand -hex 24)"
  printf 'Trax__BroadcastSigningKey=%s\n' "$(openssl rand -base64 32)"
} > "$RUNNER_TEMP_DIR/secret.env"
kubectl -n "$NS" create secret generic "$SECRET" --from-env-file="$RUNNER_TEMP_DIR/secret.env"
rm -f "$RUNNER_TEMP_DIR/secret.env"

say "first deploy"
"${TRAX[@]}" deploy --env cluster --file "$DIR/trax.deploy.ci.json"

image_of() { kubectl -n "$NS" get deployment "$1" -o jsonpath='{.spec.template.spec.containers[0].image}'; }
probe() {
  kubectl -n "$NS" port-forward service/hub "$PORT:8080" >/dev/null 2>&1 &
  forward=$!
  for _ in $(seq 30); do curl -fsS "http://127.0.0.1:$PORT/trax/health" >/dev/null 2>&1 && break; sleep 1; done
  curl -fsS "http://127.0.0.1:$PORT/trax/health" | grep -q Healthy || fail "the hub is not healthy"
  # The sample's one anonymous query, which the hub answers itself.
  answer=$(curl -fsS -H 'Content-Type: application/json' \
    -d '{"query":"{ discover { solar { monitorSolarProduction(input: {arrayId: \"SPA-001\", region: \"somerset\"}) { arrayId } } } }"}' \
    "http://127.0.0.1:$PORT/trax/graphql")
  echo "$answer" | grep -q '"arrayId":"SPA-001"' || fail "GraphQL did not answer: $answer"
  kill "$forward"; wait "$forward" 2>/dev/null || true; forward=
}
probe
hub_before=$(image_of hub)
worker_before=$(image_of worker)
echo "serving $hub_before and $worker_before"
[ "$(kubectl -n "$NS" get deployment worker -o jsonpath='{.status.readyReplicas}')" = 2 ] ||
  fail "the sample's two workers are not both ready"

say "every Trax pod is hardened and held to its service's limits"
for pod in $(kubectl -n "$NS" get pods -l 'app.kubernetes.io/name in (hub,worker)' -o name); do
  spec=$(kubectl -n "$NS" get "$pod" -o json)
  [ "$(echo "$spec" | jq -r '.spec.hostUsers')" = false ] || fail "$pod shares the node's user namespace"
  [ "$(echo "$spec" | jq -r '.spec.automountServiceAccountToken')" = false ] || fail "$pod mounts a token"
  [ "$(echo "$spec" | jq -r '.spec.containers[0].securityContext.readOnlyRootFilesystem')" = true ] ||
    fail "$pod has a writable root"
  echo "$pod: $(echo "$spec" | jq -c '.spec.containers[0].resources.limits')"
done
[ "$(kubectl -n "$NS" get deployment hub -o jsonpath='{.spec.template.spec.containers[0].resources.limits.memory}')" = 512Mi ] ||
  fail "the hub is not held to its 512 MiB"
[ "$(kubectl -n "$NS" get deployment worker -o jsonpath='{.spec.template.spec.containers[0].resources.limits.cpu}')" = 500m ] ||
  fail "a worker is not held to its half a CPU"
[ "$(kubectl -n "$NS" get statefulset database -o jsonpath='{.spec.template.spec.containers[0].resources.limits.memory}')" = 768Mi ] ||
  fail "Postgres is not held to its limits.database"
[ "$(kubectl -n "$NS" get deployment rabbitmq -o jsonpath='{.spec.template.spec.containers[0].resources.limits.memory}')" = 512Mi ] ||
  fail "RabbitMQ is not held to its limits.rabbitmq"

say "a pod that is not the app's cannot reach its database or its broker"
# The namespace's default-deny NetworkPolicy admits only the app's own pods. k3s enforces NetworkPolicy.
probe_pod() {
  kubectl -n "$NS" run "$1" --restart=Never --rm -i --quiet --image="$BUSYBOX" --labels="$2" \
    --overrides='{"spec":{"securityContext":{"runAsNonRoot":true,"runAsUser":65534,"seccompProfile":{"type":"RuntimeDefault"}},"containers":[{"name":"'"$1"'","image":"'"$BUSYBOX"'","command":["nc","-z","-w","5","'"$3"'","'"$4"'"],"securityContext":{"allowPrivilegeEscalation":false,"capabilities":{"drop":["ALL"]}}}]}}'
}
for target in "database 5432" "rabbitmq 5672"; do
  set -- $target
  # The control: the same probe labelled as the app gets through, so a refusal is the policy, not the probe.
  probe_pod insider "trax/app=$APP" "$1" "$2" >/dev/null 2>&1 || fail "the probe could not reach $1:$2 even as the app"
  if probe_pod outsider "role=outsider" "$1" "$2" >/dev/null 2>&1; then fail "a pod outside the app reached $1:$2"; fi
  echo "an app pod reached $1:$2; an outside pod was refused"
done

psql() { kubectl -n "$NS" exec statefulset/database -- psql -U "$APP" -d "$APP" -tAq -c "$1"; }
pod_ips() { kubectl -n "$NS" get pods -l "app.kubernetes.io/name=$1" --field-selector=status.phase=Running \
  -o jsonpath='{range .items[*]}{.status.podIP}{"\n"}{end}'; }
broker_peers() { kubectl -n "$NS" exec deployment/rabbitmq -- rabbitmqctl -q list_connections peer_host 2>/dev/null |
  tr -d '\r' | grep -v '^peer_host$' | sort -u; }
# The hub queues the schedule's jobs and runs none; a worker claims each and runs it, and publishes its events to the
# broker, which carries them to the hub. A finished run, a worker connected to the broker and the hub connected to it
# show that path is whole.
broker_check() {
  local since=$1 run= peers=
  for _ in $(seq 150); do
    run=$(psql "SELECT id FROM trax.metadata WHERE train_state = 'completed' AND name LIKE '%EnergyHub%'
      AND start_time > '$since' LIMIT 1")
    [ -n "$run" ] && break
    sleep 2
  done
  [ -n "$run" ] || fail "no sample train finished on a worker after $since"
  for _ in $(seq 30); do
    peers=$(broker_peers)
    comm -12 <(echo "$peers") <(pod_ips worker | sort -u) | grep -q . &&
      comm -12 <(echo "$peers") <(pod_ips hub | sort -u) | grep -q . && break
    sleep 2
  done
  comm -12 <(echo "$peers") <(pod_ips worker | sort -u) | grep -q . || fail "no worker is connected to the broker"
  comm -12 <(echo "$peers") <(pod_ips hub | sort -u) | grep -q . || fail "the hub is not connected to the broker"
  echo "run $run finished on a worker; the hub and a worker are connected to RabbitMQ ($(echo "$peers" | wc -l) connections)"
}

say "a worker runs the hub's schedule and reports through RabbitMQ"
broker_check "1970-01-01"

say "a deploy drains: it waits for a run live on a worker, then rolls the hub with no downtime"
# A run that is InProgress now. The sample's runs end in about a second, so a finished one is put back to InProgress
# here; it stands for a long run the deploy must not cut off, and is ended by hand below.
live=$(psql "UPDATE trax.metadata SET train_state = 'in_progress', end_time = NULL
  WHERE id = (SELECT max(id) FROM trax.metadata WHERE train_state = 'completed' AND name LIKE '%EnergyHub%')
  RETURNING id")
[ -n "$live" ] || fail "no finished sample run to stand in for a live one"
echo "run $live is live"

# Probe the hub through its Service from inside the namespace, five times a second, for the whole deploy. The probe is
# labelled as the app, which the namespace's NetworkPolicy admits; a port-forward would follow one pod and break.
kubectl -n "$NS" run hub-probe --restart=Never --labels="trax/app=$APP" --image="$BUSYBOX" \
  --overrides='{"spec":{"securityContext":{"runAsNonRoot":true,"runAsUser":65534,"seccompProfile":{"type":"RuntimeDefault"}},"containers":[{"name":"hub-probe","image":"'"$BUSYBOX"'","command":["sh","-c","while true; do if wget -q -T 2 -O /dev/null http://hub:8080/trax/health; then echo ok; else echo fail; fi; sleep 0.2; done"],"securityContext":{"allowPrivilegeEscalation":false,"capabilities":{"drop":["ALL"]}}}]}}' \
  >/dev/null
kubectl -n "$NS" wait --for=condition=Ready pod/hub-probe --timeout=60s >/dev/null
# How many hub pods run at once during the roll: two, while the new one comes up beside the old.
( most=0; while :; do
    n=$(kubectl -n "$NS" get pods -l app.kubernetes.io/name=hub --field-selector=status.phase=Running -o name | wc -l)
    [ "$n" -gt "$most" ] && most=$n && echo "$most" > "$RUNNER_TEMP_DIR/hubs"
    sleep 0.5
  done ) &
watcher=$!

"${TRAX[@]}" deploy --env cluster --file "$DIR/trax.deploy.ci.json" > "$RUNNER_TEMP_DIR/drain-deploy.log" 2>&1 &
deploy=$!
# Wait until the drain Job reports the run busy, hold it a while longer, then end the run. The deploy builds both
# images first, which on a fresh CI runner takes minutes before the drain starts.
for _ in $(seq 900); do
  kubectl -n "$NS" logs -l app.kubernetes.io/name=drain --tail=20 2>/dev/null | grep -q 'still busy' && break
  kill -0 "$deploy" 2>/dev/null || { cat "$RUNNER_TEMP_DIR/drain-deploy.log"; fail "the deploy ended before it drained"; }
  sleep 1
done
kubectl -n "$NS" logs -l app.kubernetes.io/name=drain --tail=20 | grep 'still busy' | tail -n 2 ||
  { tail -n 30 "$RUNNER_TEMP_DIR/drain-deploy.log"; fail "the drain Job never reported the live run"; }
unrolled() { [ "$(image_of hub)" = "$hub_before" ] && [ "$(image_of worker)" = "$worker_before" ]; }
unrolled || fail "a service rolled while a run was still busy"
held=$(date +%s)
sleep 15
unrolled || fail "a service rolled while a run was still busy"
psql "UPDATE trax.metadata SET train_state = 'completed', end_time = now() WHERE id = $live" >/dev/null
echo "the roll waited $(( $(date +%s) - held ))s after the drain first saw run $live busy; it has ended now"
wait "$deploy" || { cat "$RUNNER_TEMP_DIR/drain-deploy.log"; fail "the deploy after the drain failed"; }
tail -n 4 "$RUNNER_TEMP_DIR/drain-deploy.log"
sleep 5
kill "$watcher" 2>/dev/null || true; wait "$watcher" 2>/dev/null || true

hub_drained=$(image_of hub)
worker_drained=$(image_of worker)
[ "$hub_drained" != "$hub_before" ] || fail "the hub did not roll after the drain"
[ "$worker_drained" != "$worker_before" ] || fail "the workers did not roll after the drain"
[ "$(psql "SELECT train_state FROM trax.metadata WHERE id = $live")" = completed ] ||
  fail "run $live did not end as it was ended: the new hub's recovery reached it"
[ -n "$(psql "SELECT 1 FROM trax.drain")" ] && fail "the drain was not ended after the roll"
hubs=$(cat "$RUNNER_TEMP_DIR/hubs" 2>/dev/null || echo 0)
[ "$hubs" -ge 2 ] || fail "the old and the new hub never ran at once (most seen: $hubs)"
probes=$(kubectl -n "$NS" logs pod/hub-probe)
oks=$(echo "$probes" | grep -c '^ok$' || true)
fails=$(echo "$probes" | grep -c '^fail$' || true)
kubectl -n "$NS" delete pod hub-probe --wait=false >/dev/null
echo "health probes during the deploy: $oks answered, $fails failed; $hubs hubs ran at once"
[ "$fails" -eq 0 ] || fail "the hub was unreachable during the roll ($fails of $((oks + fails)) probes failed)"
hub_before=$hub_drained
worker_before=$worker_drained

say "a release that cannot start is rolled back"
# Kestrel reads its endpoint when the host starts serving, so the migration and the drain, which run the same image
# with the same settings and serve nothing, pass; the new hub and workers then fail at start.
jq '.settings += ["Kestrel__Endpoints__Http__Url"]
  | .environments.cluster.settings.Kestrel__Endpoints__Http__Url = "not-a-url"' "$DIR/trax.deploy.ci.json" \
  > "$DIR/trax.deploy.broken.json"
set +e
out=$("${TRAX[@]}" deploy --env cluster --file "$DIR/trax.deploy.broken.json" 2>&1)
code=$?
set -e
echo "$out" | tail -n 5
[ "$code" -ne 0 ] || fail "a release that cannot start deployed"
echo "$out" | grep -Eq 'Deployment (hub|worker) did not roll out' || fail "the failure does not name a service"
echo "$out" | grep -q 'rolled back to the version it ran before this deploy' || fail "nothing was rolled back"
[ "$(image_of hub)" = "$hub_before" ] || fail "the hub runs $(image_of hub) after the rollback, not $hub_before"
[ "$(image_of worker)" = "$worker_before" ] || fail "the workers run $(image_of worker) after the rollback"
kubectl -n "$NS" rollout status deployment/hub --timeout=120s
kubectl -n "$NS" rollout status deployment/worker --timeout=120s
probe

say "the sample held to its egress"
# The egress image, built from this commit and pushed to the registry the cluster pulls from, named by digest as a
# release names it. The workers keep the sample's own list, which is empty: they reach Postgres and RabbitMQ and
# nothing else. The hub is given api.github.com on top of its empty list, so a listed domain is proved to open too.
docker build -q -t "$registry/$APP-egress:ci" Trax.Egress >/dev/null
docker push -q "$registry/$APP-egress:ci" >/dev/null
egress_image=$(docker inspect --format '{{index .RepoDigests 0}}' "$registry/$APP-egress:ci")
case "$egress_image" in *@sha256:*) ;; *) fail "the egress image has no digest: $egress_image" ;; esac
jq --arg i "$egress_image" --slurpfile sample "$DIR/trax.deploy.json" \
  '.egressImage = $i | .services.worker.egress = $sample[0].services.worker.egress
   | .services.hub.egress = ($sample[0].services.hub.egress + ["api.github.com"])' \
  "$DIR/trax.deploy.ci.json" > "$DIR/trax.deploy.egress.json"
[ "$(jq -c .services.worker.egress "$DIR/trax.deploy.egress.json")" = '[]' ] ||
  fail "the workers are not deployed with the sample's own egress"
held_at=$(psql "SELECT now() AT TIME ZONE 'UTC'")
"${TRAX[@]}" deploy --env cluster --file "$DIR/trax.deploy.egress.json"
# A healthy hub answering GraphQL has migrated and read Postgres through the egress.
probe
# A held worker that finished a run and is connected to the broker reached both through the egress.
broker_check "$held_at"
for svc in hub worker; do
  pod=$(kubectl -n "$NS" get pods -l "app.kubernetes.io/name=$svc" --field-selector=status.phase=Running \
    -o jsonpath='{.items[0].metadata.name}')
  inits=$(kubectl -n "$NS" get pod "$pod" -o jsonpath='{.spec.initContainers[*].name}')
  [ "$inits" = "egress-rules egress" ] || fail "the $svc's init containers are '$inits'"
  [ "$(kubectl -n "$NS" get pod "$pod" -o jsonpath='{.spec.hostUsers}')" = false ] ||
    fail "the held $svc shares the node's user namespace"
done
hub_pod=$(kubectl -n "$NS" get pods -l app.kubernetes.io/name=hub --field-selector=status.phase=Running \
  -o jsonpath='{.items[0].metadata.name}')
worker_pod=$(kubectl -n "$NS" get pods -l app.kubernetes.io/name=worker --field-selector=status.phase=Running \
  -o jsonpath='{.items[0].metadata.name}')

# kubectl debug's general profile adds SYS_PTRACE, which the admission policy refuses like any capability outside
# baseline, ephemeral containers included.
set +e
out=$(kubectl -n "$NS" debug "pod/$hub_pod" --image="$egress_image" --container=egress-ptrace --profile=general \
  --attach=false --quiet -- /usr/local/bin/trax-egress check 2>&1)
set -e
echo "$out" | grep -q "ValidatingAdmissionPolicy 'trax-egress-$NS'" ||
  fail "an ephemeral container adding SYS_PTRACE was not refused: $out"
echo "refused: an ephemeral container adding SYS_PTRACE"

# The check, as an ephemeral container in a pod, running as the app would (no capability, not the egress uid): what
# the pod's list allows opens, and an unlisted name, DNS to an outside resolver, metadata, the private ranges, the
# names in the deny list and nft do not.
egress_check() {
  local pod=$1 allow=$2 deny=$3 code=
  printf '%s\n' '{"env":[{"name":"TRAX_EGRESS_ALLOW","value":"'"$allow"'"},' \
    '{"name":"TRAX_EGRESS_CHECK_DENY","value":"'"$deny"'"}],' \
    '"securityContext":{"runAsUser":1654,"runAsGroup":1654,"runAsNonRoot":true,"allowPrivilegeEscalation":false,' \
    '"capabilities":{"drop":["ALL"]},"seccompProfile":{"type":"RuntimeDefault"}}}' \
    > "$RUNNER_TEMP_DIR/egress-check.json"
  kubectl -n "$NS" debug "pod/$pod" --image="$egress_image" --container=egress-check --profile=restricted \
    --custom="$RUNNER_TEMP_DIR/egress-check.json" --attach=false --quiet -- /usr/local/bin/trax-egress check
  for _ in $(seq 90); do
    code=$(kubectl -n "$NS" get pod "$pod" \
      -o jsonpath='{.status.ephemeralContainerStatuses[?(@.name=="egress-check")].state.terminated.exitCode}')
    [ -n "$code" ] && break
    sleep 2
  done
  kubectl -n "$NS" logs "$pod" -c egress-check || true
  [ "$code" = 0 ] || fail "the egress check in $pod exited '$code': $(kubectl -n "$NS" get pod "$pod" \
    -o jsonpath='{.status.ephemeralContainerStatuses[?(@.name=="egress-check")].state}')"
  rm -f "$RUNNER_TEMP_DIR/egress-check.json"
}
egress_check "$hub_pod" "api.github.com tcp:database:5432 tcp:rabbitmq:5672" ""
# The worker's list is empty: the domain the hub may reach is refused to it.
egress_check "$worker_pod" "tcp:database:5432 tcp:rabbitmq:5672" "api.github.com:443"

# The admission policy: NET_ADMIN on an app container, and the rules container with any other image, are refused.
refused() {
  set +e
  out=$(printf '{"apiVersion":"v1","kind":"Pod","metadata":{"name":"vap-probe"},"spec":%s}' "$1" |
    kubectl -n "$NS" apply --dry-run=server -f - 2>&1)
  set -e
  echo "$out" | grep -q "ValidatingAdmissionPolicy 'trax-egress-$NS'" ||
    fail "$2 was not refused by the policy: $out"
  echo "refused: $2"
}
refused '{"containers":[{"name":"app","image":"busybox","securityContext":{"capabilities":{"add":["NET_ADMIN"]}}}]}' \
  "an app container adding NET_ADMIN"
zero=0000000000000000000000000000000000000000000000000000000000000000
refused '{"initContainers":[{"name":"egress-rules","image":"'"$registry/$APP-egress@sha256:$zero"'","args":["rules"],"securityContext":{"capabilities":{"add":["NET_ADMIN"]}}}],"containers":[{"name":"app","image":"busybox"}]}' \
  "the rules container with another image"

say "cluster: every check passed"
