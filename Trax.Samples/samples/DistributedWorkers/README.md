# EnergyHub: a hub and distributed workers

One process schedules work and serves the API; separate worker processes run it. They share
PostgreSQL (the `background_job` table) and RabbitMQ (lifecycle events), and nothing else.

What it proves:

- The hub runs no scheduled or queued job. `OverrideSubmitter` registers `PostgresJobSubmitter`
  alone, so no local worker starts; a worker claims each job with `FOR UPDATE SKIP LOCKED`.
- A train a worker runs reaches a GraphQL subscriber on the hub, over RabbitMQ. The hub and the
  worker publish on an exchange of their own, `energyhub.lifecycle`: every process bound to an
  exchange receives every event on it, so two applications on one broker need two exchanges.
- Mutations and the `operations` namespace require the Operator role; the solar query is anonymous.
  The only Operator credential is a demo key that exists in Development alone, and the dashboard
  is served only in Development.

| Project | Role |
|---|---|
| `Trax.Samples.EnergyHub` | The trains and the scheduler topology's names |
| `Trax.Samples.EnergyHub.Hub` | GraphQL API, scheduler, dashboard; executes nothing it queues |
| `Trax.Samples.EnergyHub.Worker` | `AddTraxWorker`: polls `background_job` and runs trains |

## Run

From `Trax.Samples/`:

```bash
docker compose up -d       # Postgres 5432 (database trax_energyhub), RabbitMQ 5672 (user trax / trax123)
dotnet run --project samples/DistributedWorkers/Trax.Samples.EnergyHub.Hub      # terminal 1
dotnet run --project samples/DistributedWorkers/Trax.Samples.EnergyHub.Worker   # terminal 2
```

Dashboard: <http://localhost:5202/trax>. GraphQL IDE: <http://localhost:5202/trax/graphql>.

`dotnet run` starts each host in Development, which reads the connection strings and the port from
`appsettings.Development.json`. Deployed, they come from the environment instead (below).

## Try it

```bash
# A live read the hub answers itself (anonymous)
curl -s http://localhost:5202/trax/graphql -H "Content-Type: application/json" \
  -d '{"query":"{ discover { solar { monitorSolarProduction(input: {arrayId: \"SPA-001\", region: \"somerset\"}) { arrayId totalKwh efficiency } } } }"}'

# Queue a grid trade; the worker's console logs it, the hub's does not
curl -s http://localhost:5202/trax/graphql -H "Content-Type: application/json" \
  -H "X-Api-Key: energyhub-operator-key-do-not-use-in-production" \
  -d '{"query":"mutation { dispatch { tradeGridEnergy(input: {ratePerKwh: 0.14, maxSellPercent: 80}) { externalId workQueueId } } }"}'
```

The same commands are in the hub's `Program.cs` header, and `DocumentedExamplesTests` runs them.

## Deploy it

`trax.deploy.json` beside this file deploys the sample with `trax deploy`. trax asks each project what it needs
(the hub reports `api` and `scheduler`, a GraphQL endpoint and `/trax/health`; the worker reports `worker`; both report
Postgres and RabbitMQ) and builds the rest from that. The file says only what the code cannot:

| In the file | What it does for this app |
|---|---|
| `hub`: `memory` 512, `cpus` 1, `port` 5202 | The hub's request and limit. A hub runs one copy; on a cluster the new copy starts beside the old during a roll. `port` is where the podman runtime publishes it on `127.0.0.1`. |
| `worker`: `count` 2, `memory` 384, `cpus` 0.5 | Two workers, each held to 384 MiB and half a core. They claim jobs with `FOR UPDATE SKIP LOCKED`, so two never run the same one. |
| `egress: []` on both | Neither calls anything outside: the trains only simulate the grid and the sensors. An empty list lets each reach the deployment's own Postgres and RabbitMQ and nothing else, so code that starts calling out fails until the domain is listed. |
| `connectionStrings` | The names the hosts read: `ConnectionStrings:TraxDatabase`, and the broker under `RabbitMQ` (`RabbitMQ__Host`, `__UserName`, `__Password`, which trax sets, so the password is never inside a URI). |
| `secrets`: `Trax__BroadcastSigningKey` | The key both processes sign their events with. Outside Development the hosts refuse to start without it. |
| `settings`: `Broadcast__AllowInsecureTransport` | `true` in every environment here. The broker trax runs is reached by name on the app's own network over plain AMQP, which `UseRabbitMq` refuses unless the host allows it (`BrokerTransport`). A broker anywhere else gets an `amqps://` URI and this stays unset. |
| `limits` | Postgres's and RabbitMQ's memory and CPU, when trax runs them. |
| `drainTimeout` | How long a deploy waits for running trains before it refuses: five minutes here, twenty in production. The sample's trains take about a second. |

The environments:

- **`cluster`**: a Kubernetes cluster, the `try` profile, Postgres and RabbitMQ in the cluster, images pushed to a
  registry on `localhost:5001`. This is the one CI deploys to k3s (`.github/ci/deploy/cluster.sh`).
- **`home`**: the same manifests on one machine with no cluster, run by `podman kube play` under systemd
  (`"runtime": "podman"`). trax writes the units and prints the commands that start them.
- **`production`**: the shape of a real deployment, written down but not deployed by anything here. Postgres is one
  you run and back up (`database` names a Secret holding its connection string), three workers, a registry of yours,
  and `"exposure": "tailnet"`: the hub on your tailnet through the Tailscale operator and nowhere else. It keeps the
  `try` profile only because trax cannot name an existing broker yet, so RabbitMQ still runs in the cluster.

Outside Development no Operator key is registered and the console is not served, so a deployed hub answers the
anonymous solar query and `/trax/health`, runs the schedule on the workers, and refuses every mutation and the
`operations` namespace until you add real credentials (`AddTraxApiKeyAuth` with a key from a secret, or JWT) and a
sign-in in front of the console.

### On a cluster

From this folder, with a kubeconfig context and a registry the cluster pulls from (k3s in a VM:
[Deploy to one machine with no downtime](https://traxsharp.net/docs/reference/deploy-to-one-machine-with-no-downtime)):

```bash
kubectl create namespace energyhub-cluster
cat > energyhub-cluster.env <<EOF
POSTGRES_PASSWORD=$(openssl rand -hex 24)
RABBITMQ_PASSWORD=$(openssl rand -hex 24)
Trax__BroadcastSigningKey=$(openssl rand -base64 32)
EOF
kubectl -n energyhub-cluster create secret generic energyhub-cluster --from-env-file=energyhub-cluster.env
rm energyhub-cluster.env
trax deploy --env cluster
```

```text
Asking hub what it needs (Trax.Samples.EnergyHub.Hub/Trax.Samples.EnergyHub.Hub.csproj)...
Asking worker what it needs (Trax.Samples.EnergyHub.Worker/Trax.Samples.EnergyHub.Worker.csproj)...
Deploying energyhub to namespace energyhub-cluster in context default.
Pushing images tagged 20261124-212011-38f8311aea19 to localhost:5001...
  hub -> localhost:5001/energyhub-hub:20261124-212011-38f8311aea19
  worker -> localhost:5001/energyhub-worker:20261124-212011-38f8311aea19
Waiting for database...
Waiting for rabbitmq...
Migrating the database (job energyhub-migrate-20261124-212011-38f8311aea19)...
Rolling the services onto 20261124-212011-38f8311aea19...
Waiting for hub...
Waiting for worker...
Deployed energyhub to cluster. It is not exposed: reach a hub with 'kubectl port-forward -n energyhub-cluster service/<hub> 8080'.
```

```bash
kubectl -n energyhub-cluster port-forward service/hub 8080 &
curl -s http://localhost:8080/trax/health
curl -s http://localhost:8080/trax/graphql -H "Content-Type: application/json" \
  -d '{"query":"{ discover { solar { monitorSolarProduction(input: {arrayId: \"SPA-001\", region: \"somerset\"}) { arrayId totalKwh efficiency } } } }"}'
```

Within a couple of minutes the hub has queued the schedule and the workers have run it: the workers' logs show the
trains (`kubectl -n energyhub-cluster logs -l app.kubernetes.io/name=worker`), the hub's show none.

**A deploy over a running train.** Deploy again while a train is running and the deploy drains first: a Job stops
every host starting new work and waits for what is busy. The hub and the workers keep their old images until the
train ends, then the new hub starts beside the old one, so `/trax/health` never stops answering:

```text
Migrating the database (job energyhub-migrate-20261124-212152-38f8311aea19)...
Draining: no host starts new work while the deploy waits up to 00:05:00 for what is busy (job energyhub-drain-20261124-212152-38f8311aea19)...
Rolling the services onto 20261124-212152-38f8311aea19...
Waiting for hub...
Waiting for worker...
Ending the drain (job energyhub-resume-20261124-212152-38f8311aea19)...
Deployed energyhub to cluster. It is not exposed: reach a hub with 'kubectl port-forward -n energyhub-cluster service/<hub> 8080'.
```

The drain Job's log (`kubectl -n energyhub-cluster logs job/energyhub-drain-<tag>`) says what it is waiting for:

```text
      1 still busy, 00:04:54.1728355 left before the timeout.
```

In CI's run the roll waited until that train ended; a probe asking the Service for `/trax/health` five times a second
for the whole deploy got 332 answers and no failure, and two hubs ran at once during the roll.

**A release that cannot start.** A release whose hub never gets ready is put back. With a Kestrel endpoint that is
not a URL (something only serving reads, so the migration and the drain pass):

```text
Rolling the services onto 20261124-212300-38f8311aea19...
Waiting for hub...
Rolling the services back to the version before this deploy...
Ending the drain (job energyhub-resume-20261124-212300-38f8311aea19)...
Deployment hub did not roll out: past its progress deadline (ReplicaSet "hub-6d69b94cf9" has timed out progressing.). See 'kubectl describe deployment hub -n energyhub-cluster'. Every service was rolled back to the version it ran before this deploy; the migration stays.
```

The hub and the workers that were serving keep serving on their old images throughout.

**Egress.** With a released `trax`, the empty `egress` lists put the egress sidecar in each pod. A development build
of trax names no egress image, so give the file one (`"egressImage": "<registry>/trax-egress@sha256:..."`, built from
`Trax.Egress/`) or take the lists out. The egress image's own check, run in a worker's pod, shows what the worker may
reach:

```text
ok   tcp     database:5432                            open           (want open)
ok   tcp     rabbitmq:5672                            open           (want open)
ok   dns     example.com                              blocked        (want blocked)
ok   tcp     169.254.169.254:80                       blocked        (want blocked)
ok   tcp     api.github.com:443                       blocked        (want blocked)
ok   nft     flush ruleset                            blocked        (want blocked)
egress as intended
```

### On one machine, under podman

```bash
cat > .env <<EOF
POSTGRES_PASSWORD=$(openssl rand -hex 24)
RABBITMQ_PASSWORD=$(openssl rand -hex 24)
Trax__BroadcastSigningKey=$(openssl rand -base64 32)
EOF
chmod 600 .env
trax deploy --env home
```

```text
Building localhost/energyhub-worker:20261124-214159-f8c36ed6e99a...
Wrote .trax/deploy/podman/energyhub-home.
Wrote ~/.config/trax/energyhub-home/secret.yaml (mode 0600).
Install and start it on this host:
  mkdir -p ~/.config/containers/systemd && cp .trax/deploy/podman/energyhub-home/*.kube .trax/deploy/podman/energyhub-home/*.network ~/.config/containers/systemd/
  systemctl --user daemon-reload
  systemctl --user restart energyhub-home-database.service energyhub-home-rabbitmq.service energyhub-home-hub.service energyhub-home-worker.service
```

The next release with only new images drains first and leaves the broker and the database alone:

```text
  /usr/bin/podman kube play --replace ~/.config/trax/energyhub-home/secret.yaml && /usr/bin/podman kube play --replace --userns=nomap --network energyhub-home .trax/deploy/podman/energyhub-home/energyhub-home-drain.yaml && test "$(/usr/bin/podman wait energyhub-home-drain-pod-drain)" = 0 && systemctl --user restart energyhub-home-hub.service energyhub-home-worker.service; ...
Unchanged, left running: energyhub-home-database, energyhub-home-rabbitmq.
```

Copy the units and run the `systemctl --user` line as printed; the hub answers on `http://127.0.0.1:5202`. A later
release restarts only what changed and drains first when it restarts the hub or the worker. Postgres and RabbitMQ
keep running. The podman runtime stops a service before its new version starts, so the hub is down for its restart.
`.env` and `.trax/` are in this folder's `.gitignore`.

## Tests

```bash
TRAX_TEST_PG_PORT=5432 dotnet test tests/Trax.Samples.EnergyHub.E2E
```

The suite starts the hub and a worker in one test process against `energyhub_e2e_tests`.
`TRAX_TEST_RABBITMQ` overrides the broker URI (default `amqp://trax:trax123@localhost:5672`).

The book splits a host this way in [11. A separate worker](https://traxsharp.net/docs/book/a-separate-worker).
