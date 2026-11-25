#!/usr/bin/env bash
# Records real `trax deploy` runs for the home page's deploy replay (Trax.Website's DeployReplay): what the CLI
# printed and when, every pod or container of the app as it started, became ready and went away, a health probe of
# the app five times a second, the state of a run the deploy has to wait for, and for "who can reach what" the network
# checks and their answers. Nothing is shaped for the page here; deploy.mjs turns what this writes into the website's
# copy.
#
#   deploy.sh <shape> <target> [scenario...]
#
#   shape     a folder under deploy/ holding the trax.deploy.json to deploy, or hub-workers for the DistributedWorkers
#             sample's own (deploy/README.md lists them)
#   target    kubernetes  the current kubeconfig context, with a registry on localhost:5001 that it pulls from
#             podman      the file's "home" environment ("runtime": "podman"), its units under systemd --user
#   scenario  first-deploy release-while-busy broken-release who-can-reach-what (all four, in that order, when none
#             is named; each one after the first starts from the app the one before left running)
#
# Run it on a throwaway machine, never your own: k3s or rootless podman with systemd, the .NET SDK, docker (for the
# egress image), jq, curl and openssl. deploy/README.md says how to make one in a VM. Run from the repository root.
# Each scenario writes its raw files under Trax.Samples/scripts/recordings/out/deploy/<shape>/<target>/<scenario>/.
#
# What it does to make a run worth watching, and nothing else:
# - release-while-busy puts one finished run back to InProgress before the deploy (the template's runs end in
#   moments), so the drain has a run to wait for, and ends it by hand 15 seconds after the drain first reports it
#   busy. The page says so. It is what .github/ci/deploy/cluster.sh does to prove the drain.
# - broken-release deploys the same file with one setting every service refuses at start (a Kestrel endpoint that is
#   not a URL), as .github/ci/deploy/cluster.sh does.
# - who-can-reach-what deploys the same file with the probed service held to "egress": ["api.github.com"] (unless the
#   file already names its egress) and the egress image built from this commit, then runs the checks cluster.sh runs.
set -euo pipefail

SHAPE=${1:?usage: deploy.sh <shape> <kubernetes|podman> [scenario...]}
TARGET=${2:?usage: deploy.sh <shape> <kubernetes|podman> [scenario...]}
shift 2
SCENARIOS=("$@")
[ ${#SCENARIOS[@]} -gt 0 ] || SCENARIOS=(first-deploy release-while-busy broken-release who-can-reach-what)

HERE=Trax.Samples/scripts/recordings
# A shape is a folder under deploy/, or a sample that deploys itself.
case "$SHAPE" in
  hub-workers) DIR=Trax.Samples/samples/DistributedWorkers ;;
  *) DIR=$HERE/deploy/$SHAPE ;;
esac
FILE=$DIR/trax.deploy.json
[ -f "$FILE" ] || { echo "no $FILE" >&2; exit 1; }
APP=$(jq -r .app "$FILE")
# The service the health probe hits: the first one with a port, the one serving HTTP.
PROBE=$(jq -r '[.services | to_entries[] | select(.value.port != null) | .key][0]' "$FILE")
# The service whose pods run the CLI's drain and migration: the same one, the API's.
case "$TARGET" in
  kubernetes) ENV=cluster ;;
  podman) ENV=home ;;
  *) echo "target is kubernetes or podman" >&2; exit 1 ;;
esac
NS=$APP-$ENV
STACK=$APP-$ENV
OUT_ROOT=$HERE/out/deploy/$SHAPE/$TARGET
# The CLI is built once, before anything is recorded, so a build never sits inside a recording. No build servers or
# reused MSBuild nodes: one left over from an interrupted run can hold a lock the next build waits on.
export MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0 UseSharedCompilation=false
TRAX=(dotnet run --project Trax.Cli/src/Trax.Cli/Trax.Cli.csproj -c Release --no-launch-profile --no-build --)
BUSYBOX=docker.io/library/busybox:1.37@sha256:a7dbc319b1438115a207e11930dc4bb3e643dcb3ec0bdd694bb36cd8fad289ec
UNITS=$HOME/.config/containers/systemd
export XDG_RUNTIME_DIR=${XDG_RUNTIME_DIR:-/run/user/$(id -u)}

say() { printf '\n== %s\n' "$*" >&2; }
fail() { printf 'FAIL: %s\n' "$*" >&2; exit 1; }
now() { date +%s%3N; }

# Every line on stdin, with the time it arrived: "<ms>\t<source>\t<line>".
stamp() { while IFS= read -r line; do printf '%s\t%s\t%s\n' "$(now)" "$1" "$line"; done; }

# A process and everything it started, children first.
killtree() {
  local c
  for c in $(pgrep -P "$1" 2>/dev/null); do killtree "$c"; done
  kill "$1" 2>/dev/null || true
}

pids=()
stop_background() {
  # Each watcher is a subshell running a pipeline: its children first, so no part of the pipeline is left behind.
  for p in "${pids[@]}"; do killtree "$p"; done
  for p in "${pids[@]}"; do wait "$p" 2>/dev/null || true; done
  pids=()
}
cleanup() {
  status=$?
  stop_background
  # A deploy still running, and a run this left in progress, so the next run starts from a quiet app.
  [ -n "${BUSY_PID:-}" ] && killtree "$BUSY_PID"
  [ -n "${LIVE:-}" ] && psql_any "UPDATE trax.metadata SET train_state = 'completed', end_time = now()
    WHERE id = $LIVE AND train_state = 'in_progress'" >/dev/null 2>&1
  kubectl -n "$NS" delete pod hub-probe --ignore-not-found --wait=false >/dev/null 2>&1 || true
  rm -f "$DIR"/trax.deploy.rec-*.json
  exit "$status"
}
trap cleanup EXIT

# A copy of a file (the shape's own unless a third argument names another), beside it since its projects are named
# relative to it, changed by editing its text so it reads as the original does (the page shows it), then checked
# against the same change made by jq. What changed, in a sentence, goes beside it for the page.
edited() {
  local name=$1 filter=$2 perl=$3 base=${4:-$FILE} copy
  copy=$DIR/trax.deploy.rec-$name.json
  perl -0pe "$perl" "$base" > "$copy"
  [ "$(jq -S . "$copy")" = "$(jq -S "$filter" "$base")" ] || fail "the $name copy is not the file with $filter"
  echo "$copy"
}

# The environment given a Kestrel endpoint that is not a URL, as .github/ci/deploy/cluster.sh does: Kestrel reads it
# only when the host starts serving, so the migration and the drain (same image, same settings, serving nothing) pass
# and the new copies of every service fail at start. Made from the file the other scenarios deploy.
broken_file() {
  local base
  base=$(base_file)
  E=$1 edited broken \
    ".settings += [\"Kestrel__Endpoints__Http__Url\"] | .environments.$1.settings.Kestrel__Endpoints__Http__Url = \"not-a-url\"" \
    's/^(  "settings": \[[^\n]*?)\]/$1, "Kestrel__Endpoints__Http__Url"]/m; s/("\Q$ENV{E}\E": \{.*?\n\s*"settings": \{[^\n]*?) \}/$1, "Kestrel__Endpoints__Http__Url": "not-a-url" }/s' \
    "$base"
}

# ---------------------------------------------------------------------------------------------------------------
# What is watched while a scenario runs. Each writes into $OUT, timed by this machine's clock.

psql_k8s() { kubectl -n "$NS" exec statefulset/database -- psql -U "$APP" -d "$APP" -tAq -c "$1"; }
psql_podman() { podman exec "$STACK-database-pod-database" psql -U "$APP" -d "$APP" -tAq -c "$1"; }
psql_any() { if [ "$TARGET" = kubernetes ]; then psql_k8s "$1"; else psql_podman "$1"; fi; }

watch_pods() {
  # Every change to a pod trax manages, as kubectl sees it.
  ( kubectl -n "$NS" get pods -l app.kubernetes.io/managed-by=trax -w --output-watch-events -o json 2>/dev/null |
    jq -c --unbuffered '{type, name: .object.metadata.name, service: .object.metadata.labels["app.kubernetes.io/name"],
      phase: .object.status.phase, deleting: (.object.metadata.deletionTimestamp != null),
      image: .object.spec.containers[0].image,
      ready: ([.object.status.conditions[]? | select(.type == "Ready") | .status][0] == "True"),
      containers: [.object.status.containerStatuses[]? | {name, ready, state: (.state | keys[0])}],
      inits: [.object.status.initContainerStatuses[]? | {name, ready, state: (.state | keys[0])}]}' |
    stamp pod > "$OUT/pods.tsv" ) &
  pids+=($!)
}

watch_containers() {
  # What already runs, then every container of the stack starting, dying and being removed, with podman's own time
  # for each.
  podman ps --filter "name=^$STACK-" --format json |
    jq -c --argjson t "$(now)" '.[] | {t: $t, name: .Names[0], status: "running", image: .Image}' > "$OUT/containers.jsonl"
  ( podman events --format json --filter type=container 2>/dev/null |
    jq -c --unbuffered 'select(.Name | startswith("'"$STACK"'-")) | {t: (.timeNano / 1000000 | floor), name: .Name,
      status: .Status, image: .Image, exit: .ContainerExitCode}' >> "$OUT/containers.jsonl" ) &
  pids+=($!)
}

probe_start_k8s() {
  # Probe the app through its Service from inside the namespace, five times a second, as cluster.sh does. Labelled as
  # the app, which the namespace's NetworkPolicy admits. kubectl adds the time each line was written.
  kubectl -n "$NS" delete pod hub-probe --ignore-not-found >/dev/null 2>&1
  kubectl -n "$NS" run hub-probe --restart=Never --labels="trax/app=$APP" --image="$BUSYBOX" \
    --overrides='{"spec":{"securityContext":{"runAsNonRoot":true,"runAsUser":65534,"seccompProfile":{"type":"RuntimeDefault"}},"containers":[{"name":"hub-probe","image":"'"$BUSYBOX"'","command":["sh","-c","while true; do if wget -q -T 2 -O /dev/null http://'"$PROBE"':8080/trax/health; then echo ok; else echo fail; fi; sleep 0.2; done"],"securityContext":{"allowPrivilegeEscalation":false,"capabilities":{"drop":["ALL"]}}}]}}' \
    >/dev/null
  kubectl -n "$NS" wait --for=condition=Ready pod/hub-probe --timeout=120s >/dev/null
}
probe_collect_k8s() {
  kubectl -n "$NS" logs pod/hub-probe --timestamps > "$OUT/probes.log"
  kubectl -n "$NS" delete pod hub-probe --wait=false >/dev/null
}

probe_start_podman() {
  # The hub's port on 127.0.0.1, five times a second, from this machine: the only place it is published.
  local port
  port=$(jq -r --arg s "$PROBE" '.services[$s].port' "$FILE")
  ( while :; do
      if curl -fsS -m 2 -o /dev/null "http://127.0.0.1:$port/trax/health" 2>/dev/null; then r=ok; else r=fail; fi
      printf '%s\t%s\n' "$(now)" "$r"
      sleep 0.2
    done ) > "$OUT/probes.tsv" &
  pids+=($!)
}

watch_run() {
  # The run the deploy has to wait for, and whether a drain is on, twice a second, written when either changes.
  local id=$1
  ( last=
    while :; do
      state=$(psql_any "SELECT train_state FROM trax.metadata WHERE id = $id" 2>/dev/null | tr -d '\r') || state=
      drain=$(psql_any "SELECT count(*) FROM trax.drain" 2>/dev/null | tr -d '\r') || drain=
      if [ -n "$state" ] && [ "$state|$drain" != "$last" ]; then
        printf '%s\t%s\t%s\n' "$(now)" "$state" "$drain"
        last="$state|$drain"
      fi
      sleep 0.5
    done ) > "$OUT/run.tsv" &
  pids+=($!)
}

check() {
  # One answer to "who can reach what": group, what tried, what it tried to reach, what happened, what should have.
  printf '%s\t%s\t%s\t%s\t%s\t%s\n' "$(now)" "$1" "$2" "$3" "$4" "$5" >> "$OUT/checks.tsv"
}

begin() {
  OUT=$OUT_ROOT/$1
  rm -rf "$OUT"
  mkdir -p "$OUT"
  cp "$2" "$OUT/trax.deploy.json"
  # What this run changed from the shape's own file, read off the copy it deployed, for the page to say beside it.
  {
    if [ "$(jq -c '[.services[] | .egress]' "$2")" != "$(jq -c '[.services[] | .egress]' "$FILE")" ]; then
      if holds_egress; then
        echo "Without the services' egress, as .github/ci/deploy/podman.sh deploys this sample under podman."
      else
        echo "The $PROBE held to api.github.com."
      fi
    fi
    jq -e '.egressImage' "$2" >/dev/null &&
      echo "The egress image, built from this commit, named by digest: a development build of trax names none."
    grep -q 'not-a-url' "$2" &&
      echo "A Kestrel endpoint that is not a URL, which every service refuses when it starts serving."
    true
  } | paste -sd ' ' > "$OUT/change.txt"
  : > "$OUT/cli.tsv"
  : > "$OUT/checks.tsv"
  START=$(now)
  say "$SHAPE on $TARGET: $1"
}

finish() {
  local end
  end=$(now)
  stop_background
  jq -n --arg scenario "$1" --argjson start "$START" --argjson end "$end" --arg shape "$SHAPE" --arg target "$TARGET" \
    --arg probe "$PROBE" --arg commit "$(git rev-parse HEAD)" --arg extra "${EXTRA:-{\}}" \
    '{scenario: $scenario, shape: $shape, target: $target, probe: $probe, start: $start, end: $end, commit: $commit}
      + ($extra | fromjson)' > "$OUT/meta.json"
  EXTRA='{}'
}

# trax deploy with each line timed. Its exit code is left in $DEPLOY_CODE.
deploy() {
  local file=$1
  set +e
  "${TRAX[@]}" deploy --env "$ENV" --file "$file" 2>&1 | stamp trax >> "$OUT/cli.tsv"
  DEPLOY_CODE=${PIPESTATUS[0]}
  set -e
}

# A shell command from the install, as a person would type it: the command line, then what it printed.
shell() {
  printf '%s\t%s\t%s\n' "$(now)" you "$*" >> "$OUT/cli.tsv"
  set +e
  bash -c "$*" 2>&1 | stamp shell >> "$OUT/cli.tsv"
  local code=${PIPESTATUS[0]}
  set -e
  return "$code"
}

completed_run() {
  # A finished run of one of the app's own workflows (not the scheduler's own), put back to InProgress to stand for a
  # long one. The id and the run's name.
  local row
  for _ in $(seq 60); do
    row=$(psql_any "UPDATE trax.metadata SET train_state = 'in_progress', end_time = NULL
      WHERE id = (SELECT max(id) FROM trax.metadata WHERE train_state = 'completed'
        AND name NOT LIKE 'Trax.Scheduler.%' AND parent_id IS NULL)
      RETURNING id || '|' || name" 2>/dev/null | tr -d '\r' | head -n 1) || row=
    [ -n "$row" ] && { echo "$row"; return; }
    sleep 2
  done
  fail "no finished run to stand in for a live one"
}

drain_says_busy() {
  # Only this scenario's drain: an earlier one's log says "still busy" too. Read whole, then searched, so grep -q
  # cannot cut the read short (pipefail would count that as a failure).
  local out since
  since=$(date -u -d "@$((START / 1000))" +%Y-%m-%dT%H:%M:%SZ)
  if [ "$TARGET" = kubernetes ]; then
    out=$(kubectl -n "$NS" get pods -l app.kubernetes.io/name=drain -o json 2>/dev/null |
      jq -r --arg since "$since" '.items[] | select(.metadata.creationTimestamp >= $since) | .metadata.name' |
      while read -r pod; do kubectl -n "$NS" logs "$pod" 2>/dev/null; done) || out=
  else
    out=$(podman logs --since "$since" "$STACK-drain-pod-drain" 2>/dev/null) || out=
  fi
  [[ $out == *"still busy"* ]]
}

job_logs() {
  # The drain's and the migration's own lines, with the time each was written.
  # Only this scenario's: the Jobs of earlier deploys are kept a day.
  local kind since
  since=$(date -u -d "@$((START / 1000))" +%Y-%m-%dT%H:%M:%SZ)
  for kind in migrate drain resume; do
    if [ "$TARGET" = kubernetes ]; then
      for pod in $(kubectl -n "$NS" get pods -l "app.kubernetes.io/name=$kind" -o json 2>/dev/null |
        jq -r --arg since "$since" '.items[] | select(.metadata.creationTimestamp >= $since) | "pod/" + .metadata.name'); do
        kubectl -n "$NS" logs "$pod" --timestamps 2>/dev/null | sed "s|^|$kind\t|" >> "$OUT/jobs.log" || true
      done
    else
      podman logs -t --since "$since" "$STACK-$kind-pod-$kind" 2>/dev/null | sed "s|^|$kind\t|" >> "$OUT/jobs.log" || true
    fi
  done
}

image_of() { kubectl -n "$NS" get deployment "$1" -o jsonpath='{.spec.template.spec.containers[0].image}'; }

# ---------------------------------------------------------------------------------------------------------------
# The egress image, built from this commit and named by digest, as a release names it.

EGRESS_IMAGE=
egress_image() {
  [ -n "$EGRESS_IMAGE" ] && return
  if [ "$TARGET" = kubernetes ]; then
    docker build -q -t "localhost:5001/$APP-egress:rec" Trax.Egress >/dev/null
    docker push -q "localhost:5001/$APP-egress:rec" >/dev/null
    EGRESS_IMAGE=$(docker inspect --format '{{index .RepoDigests 0}}' "localhost:5001/$APP-egress:rec")
  else
    podman build -q -t "localhost:5001/$APP-egress:rec" Trax.Egress >/dev/null
    podman push -q --tls-verify=false --digestfile "$OUT_ROOT/egress.digest" "localhost:5001/$APP-egress:rec"
    EGRESS_IMAGE=localhost:5001/$APP-egress@$(cat "$OUT_ROOT/egress.digest")
  fi
  case "$EGRESS_IMAGE" in *@sha256:*) ;; *) fail "the egress image has no digest: $EGRESS_IMAGE" ;; esac
}
holds_egress() { jq -e '[.services[] | select(.egress != null)] | length > 0' "$FILE" >/dev/null; }
# The file with the egress image, built from this commit, named after "app": a development build of trax names none.
with_egress_image() {
  IMG=$EGRESS_IMAGE edited plain '.egressImage = "'"$EGRESS_IMAGE"'"' \
    's/^(\s*)("app": [^\n]*,)$/$1$2\n$1"egressImage": "$ENV{IMG}",/m'
}
# What first-deploy, release-while-busy and broken-release deploy: the shape's own file, with the egress image named
# when it holds a service to its egress. Under podman, a file that holds services to their egress is deployed without
# it, as .github/ci/deploy/podman.sh deploys it: deployed held to its egress there, its migration did not reach
# Postgres (recorded 2026-11-24 and 2026-11-25).
base_file() {
  if ! holds_egress; then
    echo "$FILE"
  elif [ "$TARGET" = podman ]; then
    edited bare 'del(.services[].egress)' 's/,\n\s*"egress": \[[^\]\n]*\]//g'
  else
    with_egress_image
  fi
}
# What who-can-reach-what deploys: the file's own egress, or, when it names none, the probed service held to
# api.github.com; the egress image named either way.
held_file() {
  # Under podman a file that holds its services deploys without the egress here too: deployed held, its migration did
  # not reach Postgres (recorded 2026-11-25, twice), so there is no held deploy to check. The page says so.
  if holds_egress && [ "$TARGET" = podman ]; then
    base_file
    return
  fi
  if holds_egress; then
    with_egress_image
    return
  fi
  IMG=$EGRESS_IMAGE SVC=$PROBE edited held '.egressImage = "'"$EGRESS_IMAGE"'" | .services["'"$PROBE"'"].egress = ["api.github.com"]' \
    's/^(\s*)("app": [^\n]*,)$/$1$2\n$1"egressImage": "$ENV{IMG}",/m; s/^(\s*"\Q$ENV{SVC}\E": \{[^\n]*?) \}(,?)$/$1, "egress": ["api.github.com"] }$2/m'
}

# ---------------------------------------------------------------------------------------------------------------
# Kubernetes

k8s_first_deploy() {
  say "a fresh namespace and its Secret, which trax never writes"
  kubectl delete namespace "$NS" --ignore-not-found --timeout=300s >/dev/null
  kubectl delete validatingadmissionpolicybinding,validatingadmissionpolicy "trax-egress-$NS" --ignore-not-found \
    >/dev/null 2>&1 || true
  kubectl create namespace "$NS" >/dev/null
  {
    printf 'POSTGRES_PASSWORD=%s\n' "$(openssl rand -hex 24)"
    printf 'RABBITMQ_PASSWORD=%s\n' "$(openssl rand -hex 24)"
    jq -r '.secrets[]? ' "$FILE" | while read -r s; do printf '%s=%s\n' "$s" "$(openssl rand -base64 32)"; done
  } > "$OUT_ROOT/secret.env"
  kubectl -n "$NS" create secret generic "$NS" --from-env-file="$OUT_ROOT/secret.env" >/dev/null
  rm -f "$OUT_ROOT/secret.env"

  local file
  file=$(base_file)
  begin first-deploy "$file"
  watch_pods
  probe_start_k8s
  deploy "$file"
  [ "$DEPLOY_CODE" = 0 ] || fail "the first deploy failed"
  sleep 5
  probe_collect_k8s
  job_logs
  EXTRA=$(jq -n --arg image "$(image_of "$PROBE")" '{after: $image}')
  finish first-deploy
}

k8s_release_while_busy() {
  local file row id name before held after
  file=$(base_file)
  before=$(image_of "$PROBE")
  row=$(completed_run)
  id=${row%%|*}
  name=${row#*|}
  LIVE=$id
  probe_start_k8s
  begin release-while-busy "$file"
  watch_pods
  watch_run "$id"
  deploy "$file" &
  local deployer=$!
  BUSY_PID=$deployer
  for _ in $(seq 900); do drain_says_busy && break; kill -0 "$deployer" 2>/dev/null || break; sleep 1; done
  drain_says_busy || fail "the drain never reported run $id busy"
  held=$(now)
  sleep 15
  [ "$(image_of "$PROBE")" = "$before" ] || fail "$PROBE rolled while run $id was busy"
  psql_k8s "UPDATE trax.metadata SET train_state = 'completed', end_time = now() WHERE id = $id" >/dev/null
  local ended
  ended=$(now)
  wait "$deployer"
  BUSY_PID=
  LIVE=
  sleep 5
  after=$(image_of "$PROBE")
  [ "$after" != "$before" ] || fail "$PROBE did not roll"
  probe_collect_k8s
  job_logs
  EXTRA=$(jq -n --argjson id "$id" --arg name "$name" --argjson held "$held" --argjson ended "$ended" \
    --arg before "$before" --arg after "$after" \
    '{run: {id: $id, name: $name, busySeen: $held, endedByRecorder: $ended}, before: $before, after: $after}')
  finish release-while-busy
}

k8s_broken_release() {
  local file before after
  file=$(broken_file cluster)
  before=$(image_of "$PROBE")
  probe_start_k8s
  begin broken-release "$file"
  watch_pods
  deploy "$file"
  local code=$DEPLOY_CODE
  sleep 5
  after=$(image_of "$PROBE")
  [ "$code" != 0 ] || fail "a release that cannot start deployed"
  grep -q 'rolled back to the version it ran before this deploy' "$OUT/cli.tsv" ||
    fail "the broken release failed for another reason: $(tail -n 2 "$OUT/cli.tsv" | cut -f3-)"
  [ "$after" = "$before" ] || fail "$PROBE runs $after after the rollback, not $before"
  probe_collect_k8s
  EXTRA=$(jq -n --arg before "$before" --arg after "$after" --argjson code "$code" \
    '{before: $before, after: $after, exitCode: $code}')
  finish broken-release
}

probe_pod() {
  kubectl -n "$NS" run "$1" --restart=Never --rm -i --quiet --image="$BUSYBOX" --labels="$2" \
    --overrides='{"spec":{"securityContext":{"runAsNonRoot":true,"runAsUser":65534,"seccompProfile":{"type":"RuntimeDefault"}},"containers":[{"name":"'"$1"'","image":"'"$BUSYBOX"'","command":["nc","-z","-w","5","'"$3"'","'"$4"'"],"securityContext":{"allowPrivilegeEscalation":false,"capabilities":{"drop":["ALL"]}}}]}}'
}

k8s_who_can_reach_what() {
  local file pod
  file=$(held_file)
  begin who-can-reach-what "$file"
  watch_pods
  deploy "$file"
  [ "$DEPLOY_CODE" = 0 ] || fail "the held deploy failed"

  say "network policy: the app's pods reach its database and broker, others do not"
  local targets=("database 5432")
  kubectl -n "$NS" get service rabbitmq >/dev/null 2>&1 && targets+=("rabbitmq 5672")
  for t in "${targets[@]}"; do
    set -- $t
    if probe_pod insider "trax/app=$APP" "$1" "$2" >/dev/null 2>&1; then r=open; else r=refused; fi
    check policy "a pod of the app" "$1:$2" "$r" open
    if probe_pod outsider "role=outsider" "$1" "$2" >/dev/null 2>&1; then r=open; else r=refused; fi
    check policy "a pod in the namespace that is not the app's" "$1:$2" "$r" refused
  done

  say "egress: the check, run in the held pod as the app"
  pod=$(kubectl -n "$NS" get pods -l "app.kubernetes.io/name=$PROBE" --field-selector=status.phase=Running \
    -o jsonpath='{.items[0].metadata.name}')
  local allow
  allow=$(kubectl -n "$NS" get pod "$pod" -o json |
    jq -r '[.spec.initContainers[]? | select(.name == "egress") | .env[]? | select(.name == "TRAX_EGRESS_ALLOW") | .value][0]')
  printf '%s\n' '{"env":[{"name":"TRAX_EGRESS_ALLOW","value":"'"$allow"'"}],' \
    '"securityContext":{"runAsUser":1654,"runAsGroup":1654,"runAsNonRoot":true,"allowPrivilegeEscalation":false,' \
    '"capabilities":{"drop":["ALL"]},"seccompProfile":{"type":"RuntimeDefault"}}}' > "$OUT_ROOT/egress-check.json"
  kubectl -n "$NS" debug "pod/$pod" --image="$EGRESS_IMAGE" --container=egress-check --profile=restricted \
    --custom="$OUT_ROOT/egress-check.json" --attach=false --quiet -- /usr/local/bin/trax-egress check >/dev/null
  local code=
  for _ in $(seq 90); do
    code=$(kubectl -n "$NS" get pod "$pod" \
      -o jsonpath='{.status.ephemeralContainerStatuses[?(@.name=="egress-check")].state.terminated.exitCode}')
    [ -n "$code" ] && break
    sleep 2
  done
  kubectl -n "$NS" logs "$pod" -c egress-check --timestamps > "$OUT/egress-check.log" || true
  rm -f "$OUT_ROOT/egress-check.json"
  [ "$code" = 0 ] || fail "the egress check exited '$code'"

  say "admission: what the policy refuses"
  set +e
  out=$(kubectl -n "$NS" debug "pod/$pod" --image="$EGRESS_IMAGE" --container=egress-ptrace --profile=general \
    --attach=false --quiet -- /usr/local/bin/trax-egress check 2>&1)
  set -e
  if echo "$out" | grep -q "ValidatingAdmissionPolicy 'trax-egress-$NS'"; then r=refused; else r=admitted; fi
  check admission "kubectl debug, adding SYS_PTRACE" "the $PROBE pod" "$r" refused
  set +e
  out=$(printf '{"apiVersion":"v1","kind":"Pod","metadata":{"name":"vap-probe"},"spec":{"containers":[{"name":"app","image":"busybox","securityContext":{"capabilities":{"add":["NET_ADMIN"]}}}]}}' |
    kubectl -n "$NS" apply --dry-run=server -f - 2>&1)
  set -e
  if echo "$out" | grep -q "ValidatingAdmissionPolicy 'trax-egress-$NS'"; then r=refused; else r=admitted; fi
  check admission "an app container adding NET_ADMIN" "a new pod" "$r" refused
  sleep 2
  job_logs
  EXTRA=$(jq -n --arg pod "$pod" --arg allow "$allow" '{heldPod: $pod, allow: $allow}')
  finish who-can-reach-what
}

# ---------------------------------------------------------------------------------------------------------------
# podman: the units trax writes, installed and started as its output says

podman_down() {
  systemctl --user stop "$STACK-*.service" 2>/dev/null || true
  systemctl --user reset-failed "$STACK-*.service" 2>/dev/null || true
  rm -f "$UNITS/$STACK"*.kube "$UNITS/$STACK.network"
  systemctl --user daemon-reload 2>/dev/null || true
  podman pod rm -f -a --filter "name=$STACK" >/dev/null 2>&1 || true
  podman network rm -f "$STACK" >/dev/null 2>&1 || true
  podman volume rm -f "$STACK-data" >/dev/null 2>&1 || true
  rm -rf "$DIR/.trax" "${XDG_CONFIG_HOME:-$HOME/.config}/trax/$STACK"
}

# The deploy, then every command it printed for the install, run in order as printed, each line timed.
podman_release() {
  local file=$1 from cmd
  from=$(wc -l < "$OUT/cli.tsv")
  deploy "$file"
  [ "$DEPLOY_CODE" = 0 ] || return 1
  while IFS= read -r cmd; do
    shell "$cmd" || return 1
  done < <(tail -n +"$((from + 1))" "$OUT/cli.tsv" | cut -f3- |
    grep -E '^\s+(mkdir|cp|systemctl|\S*podman) ' | sed 's/^\s*//')
}

hub_active() { systemctl --user is-active --quiet "$STACK-$PROBE.service"; }
healthy() {
  local port
  port=$(jq -r --arg s "$PROBE" '.services[$s].port' "$FILE")
  curl -fsS -m 2 "http://127.0.0.1:$port/trax/health" 2>/dev/null | grep -q Healthy
}

podman_first_deploy() {
  podman_down
  mkdir -p "$UNITS"
  {
    printf 'POSTGRES_PASSWORD=%s\n' "$(openssl rand -hex 24)"
    printf 'RABBITMQ_PASSWORD=%s\n' "$(openssl rand -hex 24)"
    jq -r '.secrets[]? ' "$FILE" | while read -r s; do printf '%s=%s\n' "$s" "$(openssl rand -base64 32)"; done
  } > "$DIR/.env"
  chmod 600 "$DIR/.env"
  local file
  file=$(base_file)
  begin first-deploy "$file"
  watch_containers
  probe_start_podman
  podman_release "$file" || fail "the first deploy failed"
  for _ in $(seq 120); do healthy && break; sleep 1; done
  healthy || fail "the hub never answered"
  sleep 5
  job_logs
  finish first-deploy
}

podman_release_while_busy() {
  local file row id name held ended started
  file=$(base_file)
  row=$(completed_run)
  id=${row%%|*}
  name=${row#*|}
  LIVE=$id
  started=$(podman inspect --format '{{.State.StartedAt}}' "$STACK-$PROBE-pod-$PROBE")
  begin release-while-busy "$file"
  watch_containers
  probe_start_podman
  watch_run "$id"
  podman_release "$file" &
  local installer=$!
  BUSY_PID=$installer
  for _ in $(seq 900); do drain_says_busy && break; kill -0 "$installer" 2>/dev/null || break; sleep 1; done
  drain_says_busy || fail "the drain never reported run $id busy"
  held=$(now)
  sleep 15
  [ "$(podman inspect --format '{{.State.StartedAt}}' "$STACK-$PROBE-pod-$PROBE")" = "$started" ] ||
    fail "$PROBE restarted while run $id was busy"
  psql_podman "UPDATE trax.metadata SET train_state = 'completed', end_time = now() WHERE id = $id" >/dev/null
  ended=$(now)
  wait "$installer" || fail "the drained release did not install"
  BUSY_PID=
  LIVE=
  for _ in $(seq 120); do healthy && break; sleep 1; done
  sleep 5
  job_logs
  EXTRA=$(jq -n --argjson id "$id" --arg name "$name" --argjson held "$held" --argjson ended "$ended" \
    '{run: {id: $id, name: $name, busySeen: $held, endedByRecorder: $ended}}')
  finish release-while-busy
}

podman_broken_release() {
  local file
  file=$(broken_file home)
  begin broken-release "$file"
  watch_containers
  probe_start_podman
  local code=0
  podman_release "$file" || code=$?
  # The restart does not come back: record a minute of it, then what systemd says of the unit.
  sleep 60
  shell "systemctl --user is-active $STACK-$PROBE.service" || true
  EXTRA=$(jq -n --argjson code "$code" '{installExit: $code}')
  finish broken-release
  # Put the working version back for the next scenario, outside the recording.
  OUT=$OUT_ROOT/.restore
  mkdir -p "$OUT"
  : > "$OUT/cli.tsv"
  podman_release "$(base_file)" || fail "the working version did not come back"
  for _ in $(seq 120); do healthy && break; sleep 1; done
  rm -rf "$OUT_ROOT/.restore"
}

podman_who_can_reach_what() {
  local file port addr
  file=$(held_file)
  begin who-can-reach-what "$file"
  watch_containers
  podman_release "$file" || fail "the held deploy failed"
  # Five minutes: systemd starts the unit again when its first start fails, and the recording keeps both.
  for _ in $(seq 300); do healthy && break; sleep 1; done
  healthy || fail "the held hub never answered"
  port=$(jq -r --arg s "$PROBE" '.services[$s].port' "$FILE")

  say "what is published: the hub on loopback, nothing else"
  if curl -fsS -m 3 -o /dev/null "http://127.0.0.1:$port/trax/health"; then r=open; else r=refused; fi
  check published "this machine, on loopback" "the $PROBE, 127.0.0.1:$port" "$r" open
  addr=$(ip -4 -o addr show scope global | awk '{print $4}' | cut -d/ -f1 | head -n 1)
  if curl -fsS -m 3 -o /dev/null "http://$addr:$port/trax/health"; then r=open; else r=refused; fi
  check published "another machine on the network" "the $PROBE, $addr:$port" "$r" refused
  if timeout 3 bash -c "</dev/tcp/127.0.0.1/5432" 2>/dev/null; then r=open; else r=refused; fi
  check published "this machine" "Postgres, 127.0.0.1:5432" "$r" refused
  local db_ip
  db_ip=$(podman inspect "$STACK-database-pod-database" --format '{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}')
  if podman run --rm --network podman "$BUSYBOX" nc -z -w 3 "$db_ip" 5432 >/dev/null 2>&1; then r=open; else r=refused; fi
  check network "a container outside the app's network" "Postgres, $db_ip:5432" "$r" refused

  if ! jq -e --arg s "$PROBE" '.services[$s].egress != null' "$file" >/dev/null; then
    EXTRA='{}'
    finish who-can-reach-what
    return
  fi
  say "egress: the check, run in the held pod as the app"
  local pod allow
  pod=$(podman ps --filter "name=^$STACK-$PROBE-pod-$PROBE\$" --format '{{.Pod}}')
  allow=$(podman inspect "$STACK-$PROBE-pod-egress" --format '{{range .Config.Env}}{{println .}}{{end}}' |
    sed -n 's/^TRAX_EGRESS_ALLOW=//p')
  podman run --rm --pod "$pod" --user 1654:1654 --cap-drop=ALL --security-opt no-new-privileges \
    -e "TRAX_EGRESS_ALLOW=$allow" --entrypoint /usr/local/bin/trax-egress "$EGRESS_IMAGE" check 2>&1 |
    while IFS= read -r line; do printf '%s %s\n' "$(date -u +%Y-%m-%dT%H:%M:%S.%3NZ)" "$line"; done > "$OUT/egress-check.log"
  EXTRA=$(jq -n --arg allow "$allow" '{allow: $allow}')
  finish who-can-reach-what
}

# ---------------------------------------------------------------------------------------------------------------

mkdir -p "$OUT_ROOT"
dotnet build Trax.Cli/src/Trax.Cli/Trax.Cli.csproj -c Release -v quiet -nologo >/dev/null
{
  echo "kernel $(uname -r)"
  [ "$TARGET" = kubernetes ] && kubectl version -o json | jq -r '"kubernetes " + .serverVersion.gitVersion'
  [ "$TARGET" = podman ] && podman version --format 'podman {{.Client.Version}}'
  echo "dotnet $(dotnet --version)"
} > "$OUT_ROOT/versions.txt"

# The egress image is built here, in this shell, since the copies of the file that name it are made in subshells.
if [[ " ${SCENARIOS[*]} " == *" who-can-reach-what "* ]] ||
  jq -e '[.services[] | select(.egress != null)] | length > 0' "$FILE" >/dev/null; then
  egress_image
fi

for s in "${SCENARIOS[@]}"; do
  fn=${TARGET/kubernetes/k8s}_${s//-/_}
  declare -F "$fn" >/dev/null || fail "no scenario $s"
  "$fn"
done
[ "$TARGET" = podman ] && podman_down
say "$SHAPE on $TARGET: recorded ${SCENARIOS[*]}"
