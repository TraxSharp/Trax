# Deploy proof

`trax deploy` run for real, not against a fake: `cluster.sh` on a Kubernetes cluster and `podman.sh` with the
podman runtime under systemd. Together they are the `deploy-cluster` CI job, which installs k3s on the runner
(`K8S=k3s`). Elsewhere `cluster.sh` uses the current kubeconfig context and the registry in `TRAX_CI_REGISTRY`,
which is how the same checks run against k3s in a VM.

The app is the `trax-hub` template project as it sits in the repository, so it builds against the code in the same
commit. `trax.deploy.json` here deploys it twice: `cluster` (a Kubernetes cluster) and `home` (the podman runtime).

What `cluster.sh` proves, in order:

1. a first deploy: the namespace's Secret is checked, Postgres comes up in the cluster under its `limits`, the
   migrate Job runs, the hub rolls out, `/trax/health` answers and GraphQL answers a query;
2. every Trax pod runs with a read-only root, `hostUsers: false` and no service account token, and the namespace's
   default-deny NetworkPolicy holds: a pod labelled as the app reaches Postgres, one that is not is refused;
3. a release that cannot start (a setting the hub refuses at start) is rolled back: the deploy fails at the
   Deployment's progress deadline naming the hub, and the hub that was serving serves again on the old image.
4. the hub held to its egress (`"egress": ["api.github.com"]`, the egress image built from the same commit and
   named by digest): it rolls out healthy and answers GraphQL, so it reached Postgres through the egress; its pod
   has the rules init container and the egress sidecar and still no user namespace of the node's; the egress check,
   run in the hub's pod as an ephemeral container with the app's uid, finds api.github.com and the database open and
   everything else (other names, DNS to an outside resolver, metadata, private ranges, `nft flush ruleset`) refused;
   and the admission policy refuses NET_ADMIN on an app container and on a rules container running another image.

What `podman.sh` proves:

1. the first deploy builds the image into podman and writes units that, installed as printed, start Postgres, run
   the migration and serve a healthy hub on `127.0.0.1` with a read-only root;
2. a release that changes only the image restarts only the hub, and Postgres keeps running.

Not kind: a kind node is a container, so a pod there cannot have a user namespace of its own, and every Trax pod asks
for one (`hostUsers: false`). The pods never start; `trax deploy` says so at once.
