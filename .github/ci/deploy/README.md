# Deploy proof

`trax deploy` run for real, not against a fake: `cluster.sh` on a Kubernetes cluster and `podman.sh` with the
podman runtime under systemd. Together they are the `deploy-cluster` CI job, which installs k3s on the runner
(`K8S=k3s`). Elsewhere `cluster.sh` uses the current kubeconfig context and the registry in `TRAX_CI_REGISTRY`,
which is how the same checks run against k3s in a VM.

The app is the DistributedWorkers sample, `Trax.Samples/samples/DistributedWorkers`: the EnergyHub hub (GraphQL and
the scheduler, running no jobs), its workers (which claim the jobs from Postgres and publish their events to
RabbitMQ), Postgres and RabbitMQ. It builds against the code in the same commit. Both scripts deploy the sample's own
`trax.deploy.json` (`cluster` and `home`), from a copy beside it that changes only what a run must: the registry and
namespace, and the services' `egress`, which the copies leave out until the egress image is built.

What `cluster.sh` proves, in order:

1. a first deploy: the namespace's Secret is checked, Postgres and RabbitMQ come up in the cluster under their
   `limits`, the migrate Job runs, the hub and both workers roll out, `/trax/health` answers and GraphQL answers the
   sample's anonymous query;
2. every Trax pod runs with a read-only root, `hostUsers: false`, no service account token and its service's
   `memory` and `cpus`, and the namespace's default-deny NetworkPolicy holds: a pod labelled as the app reaches
   Postgres and RabbitMQ, one that is not is refused by both;
3. the sample works as split: a worker finishes a run of the hub's schedule, and the hub and a worker are both
   connected to RabbitMQ (their pod addresses among the broker's connections);
4. a deploy drains before it rolls: with a run InProgress (a finished run put back, standing for a long one), the
   drain Job reports it busy and neither the hub's nor the workers' image changes until the run ends; then the hub
   rolls with the new copy beside the old (two hub pods at once), a probe inside the namespace hits `/trax/health`
   through the Service five times a second for the whole deploy and no probe fails, the run is still the way it was
   ended (the new hub's recovery did not touch it), and the drain row is gone;
5. a release that cannot start (a Kestrel endpoint that is not a URL, which only serving reads, so the migration and
   the drain pass) is rolled back: the deploy fails naming the service, and the hub and the workers that were
   serving serve again on the old images;
6. the services held to their egress (the egress image built from the same commit and named by digest): the workers
   keep the sample's empty list and the hub is given `api.github.com` on top of it. The hub rolls out healthy and
   answers GraphQL, so it reached Postgres through the egress, and a worker finishes a new run and is connected to
   RabbitMQ, so it reached both through its egress. Both pods have the rules init container and the egress sidecar
   and still no user namespace of the node's. The egress check, run in each pod as an ephemeral container with the
   app's uid, finds what that pod's list allows open (the hub: api.github.com, the database and the broker; a worker:
   the database and the broker) and everything else refused (other names, `api.github.com` from a worker, DNS to an
   outside resolver, metadata, private ranges, `nft flush ruleset`); and the admission policy refuses SYS_PTRACE on
   an ephemeral container, NET_ADMIN on an app container and a rules container running another image.

What `podman.sh` proves:

1. the first deploy builds the images into podman and writes units that, installed as printed, start Postgres and
   RabbitMQ, run the migration and serve a healthy hub on `127.0.0.1` and a worker, both with a read-only root;
2. a worker finishes a run of the hub's schedule, and the hub and the worker are both connected to RabbitMQ;
3. a release that changes only the images drains first: with a run InProgress, the drain pod reports it busy and
   neither the hub nor the worker is restarted until the run ends; then only they restart, Postgres and RabbitMQ keep
   running, the run is left as it was ended and the drain is ended. The podman runtime has no overlap, so the hub is
   down for its restart; that is not measured here.

`podman kube play` runs one copy of a Deployment, so the sample's two workers are one under podman. The egress is not
proved under podman here: the script builds no egress image into podman.

Not kind: a kind node is a container, so a pod there cannot have a user namespace of its own, and every Trax pod asks
for one (`hostUsers: false`). The pods never start; `trax deploy` says so at once.
