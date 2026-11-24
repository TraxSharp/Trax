#!/usr/bin/env bash
# Deploys .github/ci/deploy/trax.deploy.json's cluster environment to a real Kubernetes cluster and checks what it
# did. Needs kubectl and the .NET SDK. Run from the repository root.
#
#   K8S=k3s       installs k3s on this machine (root, through sudo) with a registry on 127.0.0.1:5001 that it pulls
#                 from, and uninstalls both at the end. For a throwaway machine: the deploy-cluster CI job.
#   K8S=existing  (default) uses the current kubeconfig context and the registry in TRAX_CI_REGISTRY, such as k3s in
#                 a VM; it creates and deletes only its namespace (TRAX_CI_NAMESPACE, traxci-cluster by default) and
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
DIR=.github/ci/deploy
NS=${TRAX_CI_NAMESPACE:-traxci-cluster}
# The app's Secret is named after the app and environment, wherever the namespace is.
SECRET=traxci-cluster
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
    kubectl -n "$NS" logs -l app.kubernetes.io/part-of=traxci --all-containers --tail=50 2>/dev/null || true
  fi
  [ -n "${forward:-}" ] && kill "$forward" 2>/dev/null || true
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
  # The node registers a few seconds after k3s starts; wait for it to exist, then to be ready.
  for _ in $(seq 60); do [ -n "$(kubectl get nodes -o name 2>/dev/null)" ] && break; sleep 2; done
  kubectl wait --for=condition=Ready node --all --timeout=120s
  registry=localhost:5001
else
  registry=${TRAX_CI_REGISTRY:?set TRAX_CI_REGISTRY to a registry the cluster pulls from}
fi
kubectl version >/dev/null

# The copies sit beside the original, since its projects are named relative to it.
jq --arg r "$registry" --arg ns "$NS" '.environments.cluster.registry = $r | .environments.cluster.namespace = $ns' \
  "$DIR/trax.deploy.json" > "$DIR/trax.deploy.ci.json"

say "the namespace and its Secret, which trax never writes"
# A namespace an earlier run left is waited out, so this run starts from nothing.
kubectl delete namespace "$NS" --ignore-not-found --timeout=180s
kubectl create namespace "$NS"
printf 'POSTGRES_PASSWORD=%s\n' "$(openssl rand -hex 24)" > "$RUNNER_TEMP_DIR/secret.env"
kubectl -n "$NS" create secret generic "$SECRET" --from-env-file="$RUNNER_TEMP_DIR/secret.env"
rm -f "$RUNNER_TEMP_DIR/secret.env"

say "first deploy"
"${TRAX[@]}" deploy --env cluster --file "$DIR/trax.deploy.ci.json"

probe() {
  kubectl -n "$NS" port-forward service/hub "$PORT:8080" >/dev/null 2>&1 &
  forward=$!
  for _ in $(seq 30); do curl -fsS "http://127.0.0.1:$PORT/trax/health" >/dev/null 2>&1 && break; sleep 1; done
  curl -fsS "http://127.0.0.1:$PORT/trax/health" | grep -q Healthy || fail "the hub is not healthy"
  answer=$(curl -fsS -H 'Content-Type: application/json' -d '{"query":"{ __typename }"}' "http://127.0.0.1:$PORT/trax/graphql")
  echo "$answer" | grep -q '"__typename":"RootQuery"' || fail "GraphQL did not answer: $answer"
  kill "$forward"; wait "$forward" 2>/dev/null || true; forward=
}
probe
image_before=$(kubectl -n "$NS" get deployment hub -o jsonpath='{.spec.template.spec.containers[0].image}')
echo "serving $image_before"

say "every Trax pod is hardened"
for pod in $(kubectl -n "$NS" get pods -l app.kubernetes.io/name=hub -o name); do
  spec=$(kubectl -n "$NS" get "$pod" -o json)
  [ "$(echo "$spec" | jq -r '.spec.hostUsers')" = false ] || fail "$pod shares the node's user namespace"
  [ "$(echo "$spec" | jq -r '.spec.automountServiceAccountToken')" = false ] || fail "$pod mounts a token"
  [ "$(echo "$spec" | jq -r '.spec.containers[0].securityContext.readOnlyRootFilesystem')" = true ] ||
    fail "$pod has a writable root"
done

say "a pod that is not the app's cannot reach its database"
# The namespace's default-deny NetworkPolicy admits only the app's own pods. k3s enforces NetworkPolicy.
probe_pod() {
  kubectl -n "$NS" run "$1" --restart=Never --rm -i --quiet \
    --image=docker.io/library/busybox:1.37@sha256:a7dbc319b1438115a207e11930dc4bb3e643dcb3ec0bdd694bb36cd8fad289ec \
    --labels="$2" --overrides='{"spec":{"securityContext":{"runAsNonRoot":true,"runAsUser":65534,"seccompProfile":{"type":"RuntimeDefault"}},"containers":[{"name":"'"$1"'","image":"docker.io/library/busybox:1.37@sha256:a7dbc319b1438115a207e11930dc4bb3e643dcb3ec0bdd694bb36cd8fad289ec","command":["nc","-z","-w","5","database","5432"],"securityContext":{"allowPrivilegeEscalation":false,"capabilities":{"drop":["ALL"]}}}]}}'
}
# The control: the same probe labelled as the app gets through, so a refusal above is the policy, not the probe.
probe_pod insider "trax/app=traxci" >/dev/null 2>&1 || fail "the probe could not reach Postgres even as the app"
if probe_pod outsider "role=outsider" >/dev/null 2>&1; then fail "a pod outside the app reached Postgres"; fi
echo "an app pod reached Postgres; an outside pod was refused"

say "a release that cannot start is rolled back"
jq '.environments.cluster.settings.Trax__Proxy__KnownNetworks = "not-a-network"' "$DIR/trax.deploy.ci.json" \
  > "$DIR/trax.deploy.broken.json"
set +e
out=$("${TRAX[@]}" deploy --env cluster --file "$DIR/trax.deploy.broken.json" 2>&1)
code=$?
set -e
echo "$out" | tail -n 5
[ "$code" -ne 0 ] || fail "a release that cannot start deployed"
echo "$out" | grep -q 'Deployment hub did not roll out' || fail "the failure does not name the hub"
echo "$out" | grep -q 'rolled back to the version it ran before this deploy' || fail "nothing was rolled back"
image_after=$(kubectl -n "$NS" get deployment hub -o jsonpath='{.spec.template.spec.containers[0].image}')
[ "$image_after" = "$image_before" ] || fail "the hub runs $image_after after the rollback, not $image_before"
kubectl -n "$NS" rollout status deployment/hub --timeout=120s
probe

say "a hub held to its egress"
# The egress image, built from this commit and pushed to the registry the cluster pulls from, named by digest as a
# release names it. The hub may reach api.github.com and the database, and nothing else.
docker build -q -t "$registry/traxci-egress:ci" Trax.Egress >/dev/null
docker push -q "$registry/traxci-egress:ci" >/dev/null
egress_image=$(docker inspect --format '{{index .RepoDigests 0}}' "$registry/traxci-egress:ci")
case "$egress_image" in *@sha256:*) ;; *) fail "the egress image has no digest: $egress_image" ;; esac
jq --arg i "$egress_image" '.egressImage = $i | .services.hub.egress = ["api.github.com"]' \
  "$DIR/trax.deploy.ci.json" > "$DIR/trax.deploy.egress.json"
"${TRAX[@]}" deploy --env cluster --file "$DIR/trax.deploy.egress.json"
# A healthy hub answering GraphQL has migrated and read Postgres through the egress.
probe
hub_pod=$(kubectl -n "$NS" get pods -l app.kubernetes.io/name=hub --field-selector=status.phase=Running \
  -o jsonpath='{.items[0].metadata.name}')
inits=$(kubectl -n "$NS" get pod "$hub_pod" -o jsonpath='{.spec.initContainers[*].name}')
[ "$inits" = "egress-rules egress" ] || fail "the hub's init containers are '$inits'"
[ "$(kubectl -n "$NS" get pod "$hub_pod" -o jsonpath='{.spec.hostUsers}')" = false ] ||
  fail "the held hub shares the node's user namespace"

# kubectl debug's general profile adds SYS_PTRACE, which the admission policy refuses like any capability outside
# baseline, ephemeral containers included.
set +e
out=$(kubectl -n "$NS" debug "pod/$hub_pod" --image="$egress_image" --container=egress-ptrace --profile=general \
  --attach=false --quiet -- /usr/local/bin/trax-egress check 2>&1)
set -e
echo "$out" | grep -q "ValidatingAdmissionPolicy 'trax-egress-$NS'" ||
  fail "an ephemeral container adding SYS_PTRACE was not refused: $out"
echo "refused: an ephemeral container adding SYS_PTRACE"

# The check, as an ephemeral container in the hub's pod, running as the app would (no capability, not the egress
# uid): api.github.com opens, the database opens, an unlisted name and DNS to an outside resolver do not, metadata
# and the private ranges do not, and nft cannot read or flush the rules.
printf '%s\n' '{"env":[{"name":"TRAX_EGRESS_ALLOW","value":"api.github.com tcp:database:5432"}],' \
  '"securityContext":{"runAsUser":1654,"runAsGroup":1654,"runAsNonRoot":true,"allowPrivilegeEscalation":false,' \
  '"capabilities":{"drop":["ALL"]},"seccompProfile":{"type":"RuntimeDefault"}}}' \
  > "$RUNNER_TEMP_DIR/egress-check.json"
kubectl -n "$NS" debug "pod/$hub_pod" --image="$egress_image" --container=egress-check --profile=restricted \
  --custom="$RUNNER_TEMP_DIR/egress-check.json" --attach=false --quiet -- /usr/local/bin/trax-egress check
code=
for _ in $(seq 90); do
  code=$(kubectl -n "$NS" get pod "$hub_pod" \
    -o jsonpath='{.status.ephemeralContainerStatuses[?(@.name=="egress-check")].state.terminated.exitCode}')
  [ -n "$code" ] && break
  sleep 2
done
kubectl -n "$NS" logs "$hub_pod" -c egress-check || true
[ "$code" = 0 ] || fail "the egress check in the hub's pod exited '$code': $(kubectl -n "$NS" get pod "$hub_pod" \
  -o jsonpath='{.status.ephemeralContainerStatuses[?(@.name=="egress-check")].state}')"
rm -f "$RUNNER_TEMP_DIR/egress-check.json"

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
refused '{"initContainers":[{"name":"egress-rules","image":"'"$registry/traxci-egress@sha256:$zero"'","args":["rules"],"securityContext":{"capabilities":{"add":["NET_ADMIN"]}}}],"containers":[{"name":"app","image":"busybox"}]}' \
  "the rules container with another image"

say "cluster: every check passed"
