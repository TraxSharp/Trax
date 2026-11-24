#!/usr/bin/env bash
# Deploys .github/ci/deploy/trax.deploy.json's cluster environment to a real Kubernetes cluster and checks what it
# did. Needs kubectl and the .NET SDK. Run from the repository root.
#
#   K8S=k3s       installs k3s on this machine (root, through sudo) with a registry on 127.0.0.1:5001 that it pulls
#                 from, and uninstalls both at the end. For a throwaway machine: the deploy-cluster CI job.
#   K8S=existing  (default) uses the current kubeconfig context and the registry in TRAX_CI_REGISTRY, such as k3s in
#                 a VM; it creates and deletes only its namespace.
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
NS=traxci-cluster
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
jq --arg r "$registry" '.environments.cluster.registry = $r' "$DIR/trax.deploy.json" > "$DIR/trax.deploy.ci.json"

say "the namespace and its Secret, which trax never writes"
# A namespace an earlier run left is waited out, so this run starts from nothing.
kubectl delete namespace "$NS" --ignore-not-found --timeout=180s
kubectl create namespace "$NS"
printf 'POSTGRES_PASSWORD=%s\n' "$(openssl rand -hex 24)" > "$RUNNER_TEMP_DIR/secret.env"
kubectl -n "$NS" create secret generic "$NS" --from-env-file="$RUNNER_TEMP_DIR/secret.env"
rm -f "$RUNNER_TEMP_DIR/secret.env"

say "first deploy"
"${TRAX[@]}" deploy --env cluster --file "$DIR/trax.deploy.ci.json"

probe() {
  kubectl -n "$NS" port-forward service/hub 18080:8080 >/dev/null 2>&1 &
  forward=$!
  for _ in $(seq 30); do curl -fsS http://127.0.0.1:18080/trax/health >/dev/null 2>&1 && break; sleep 1; done
  curl -fsS http://127.0.0.1:18080/trax/health | grep -q Healthy || fail "the hub is not healthy"
  answer=$(curl -fsS -H 'Content-Type: application/json' -d '{"query":"{ __typename }"}' http://127.0.0.1:18080/trax/graphql)
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

rm -f "$DIR/trax.deploy.ci.json" "$DIR/trax.deploy.broken.json"
say "cluster: every check passed"
