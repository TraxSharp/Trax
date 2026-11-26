---
title: A zero-downtime deploy on k3s
description: "Take a new trax-hub project to k3s in a virtual machine with trax deploy: a private hub, a release that drains and keeps serving, and one that rolls back."
type: tutorial
area: tooling
nav_order: 5
---

# A zero-downtime deploy on k3s

By the end of this page you have a Trax hub running on k3s inside a virtual machine, with its Postgres beside it,
reachable from nowhere but `kubectl port-forward`. You release it twice: once with a change, while a probe hits it
five times a second and sees no failure, and once with a change that cannot start, which `trax deploy` rolls back
on its own.

Each step shows what you type and what comes back. Where the output holds something that is yours (the time, a
commit), it is shown as it looked on the run this page was written from.

[How a deploy works](/docs/reference/how-a-deploy-works) explains what happens at each step and why. This page
does not stop to.

## What you need

On the machine you deploy from:

- the .NET 10 SDK, `git`, `kubectl`, `ssh` and `openssl`;
- the Trax templates and the `trax` tool:

```bash
dotnet new install Trax.Samples.Templates
dotnet tool install --global Trax.Cli
```

A virtual machine on any hypervisor, with:

- about 4 vCPUs, 8 GB of memory and 40 GB of disk;
- a Linux kernel of 6.3 or later (Debian 13 and Ubuntu 24.04 are both new enough), since every Trax pod asks for a
  user namespace of its own;
- `podman`, for a registry, and SSH access as a user who can `sudo`.

The VM is not optional. k3s runs as root, and inside a VM a container that escaped is root in a machine that holds
only this app, not the one your keys live on.

## 1. Make the app

```bash
dotnet new trax-hub -n Orders
cd Orders
echo '.trax/' >> .gitignore
git init -q && git add -A && git commit -qm "The Orders hub"
```

```text
The template "Trax Hub (API + Scheduler + Dashboard)" was created successfully.
```

The hub keeps its runs in memory until `ConnectionStrings__Trax` is set. The deploy sets it, so in the cluster it
keeps them in Postgres. `.trax/` is where `trax deploy plan` writes what it would apply; keeping it out of git keeps
the image tags clean, since a tag ends in `-dirty` when the tree has changes.

## 2. Describe the deployment

```bash
trax deploy init --target kubernetes
```

```text
Wrote /home/you/Orders/trax.deploy.json:
  hub         Orders.csproj
Name the settings and secrets the hosts read, then run 'trax deploy plan --env staging'.
```

Open `trax.deploy.json` and make it this:

```json
{
  "version": 2,
  "app": "orders",
  "services": {
    "hub": { "project": "Orders.csproj", "memory": 512 }
  },
  "settings": ["Trax__Proxy__KnownNetworks"],
  "environments": {
    "staging": {
      "target": "kubernetes",
      "profile": "try",
      "registry": "localhost:5001",
      "database": { "inCluster": true },
      "settings": { "Trax__Proxy__KnownNetworks": "10.42.0.0/16" }
    }
  }
}
```

`try` lets Postgres run in the cluster, as one copy with no backups. `registry` is where the images go and where
the cluster pulls them from. `Trax__Proxy__KnownNetworks` is the range the template's hub believes forwarded headers
from, here k3s's pod range; step 9 uses it.

Commit it, then see what a deploy would apply:

```bash
git commit -qam "Deploy to staging"
trax deploy plan --env staging
```

```text
Asking hub what it needs (Orders.csproj)...
Wrote /home/you/Orders/.trax/deploy/kubernetes/orders-staging:
  Namespace     orders-staging
  NetworkPolicy orders-deny
  NetworkPolicy orders-within
  Service       database
  StatefulSet   database
  Job           orders-migrate-20261124-150212-3f9a1c2b7d4e
  Job           orders-drain-20261124-150212-3f9a1c2b7d4e
  Service       hub
  Deployment    hub
  Job           orders-resume-20261124-150212-3f9a1c2b7d4e
They read POSTGRES_PASSWORD from the Secret orders-staging in namespace orders-staging, which trax does not write.
```

`Asking hub what it needs` is trax building the project and running it once with `--trax-export-deploy`. The hub
reported that it serves the API and runs the scheduler, keeps its runs in Postgres and maps `/trax/health`; that is
why there is one hub with a Service in front of it, and no RabbitMQ. Nothing in the file said so.

## 3. Start k3s in the VM

In the VM, as root. First a registry on the VM's loopback, and k3s told to pull `localhost:5001` from it:

```bash
podman run -d --name registry --restart=always -p 127.0.0.1:5001:5000 \
  docker.io/library/registry:3@sha256:82f6ff4a7f982960c8aa51617fea3aabc485df6e55cce80d22deb1de36f1bc3c
mkdir -p /etc/rancher/k3s
printf 'mirrors:\n  "localhost:5001":\n    endpoint: ["http://127.0.0.1:5001"]\n' > /etc/rancher/k3s/registries.yaml
```

Then k3s itself, from the installer at its release tag, checked against its digest before it runs:

```bash
curl -sSfL -o k3s-install.sh "https://raw.githubusercontent.com/k3s-io/k3s/v1.36.6%2Bk3s1/install.sh"
echo "46177d4c99440b4c0311b67233823a8e8a2fc09693f6c89af1a7161e152fbfad  k3s-install.sh" | sha256sum -c -
INSTALL_K3S_VERSION=v1.36.6+k3s1 sh k3s-install.sh --disable traefik --write-kubeconfig-mode 600
kubectl wait --for=condition=Ready node --all --timeout=120s
```

```text
k3s-install.sh: OK
node/the-vm condition met
```

`--disable traefik` leaves out the ingress controller k3s installs by default: nothing here is published. k3s
enforces NetworkPolicy with its own controller, which the deploy relies on.

## 4. Reach the VM from your machine

Back on your machine. Forward the registry and the API server over SSH, and take the cluster's kubeconfig, which
only root can read in the VM:

```bash
ssh -fN -L 5001:127.0.0.1:5001 -L 6443:127.0.0.1:6443 you@the-vm
ssh you@the-vm sudo cat /etc/rancher/k3s/k3s.yaml > ~/.kube/the-vm.yaml
chmod 600 ~/.kube/the-vm.yaml
export KUBECONFIG=~/.kube/the-vm.yaml
kubectl config current-context
```

```text
default
```

`trax deploy` applies to the context `kubectl config current-context` names and to nothing else. k3s calls its
context `default`.

## 5. The namespace and its Secret

trax writes no secret into anything it applies. Postgres's password comes from a Secret you create, named after the
app and the environment:

```bash
kubectl create namespace orders-staging
printf 'POSTGRES_PASSWORD=%s\n' "$(openssl rand -hex 24)" > ~/orders-staging.env
kubectl -n orders-staging create secret generic orders-staging --from-env-file="$HOME/orders-staging.env"
rm ~/orders-staging.env
```

```text
namespace/orders-staging created
secret/orders-staging created
```

The value goes through a file so it is never on a command line. Without the Secret, or with a key missing from it,
the deploy refuses before it applies anything and prints these commands.

## 6. Deploy

```bash
trax deploy --env staging
```

```text
Asking hub what it needs (Orders.csproj)...
Deploying orders to namespace orders-staging in context default.
Pushing images tagged 20261124-150212-3f9a1c2b7d4e to localhost:5001...
  hub -> localhost:5001/orders-hub:20261124-150212-3f9a1c2b7d4e
Waiting for database...
Migrating the database (job orders-migrate-20261124-150212-3f9a1c2b7d4e)...
Rolling the services onto 20261124-150212-3f9a1c2b7d4e...
Waiting for hub...
Deployed orders to staging. It is not exposed: reach a hub with 'kubectl port-forward -n orders-staging service/<hub> 8080'.
```

The image was built and pushed by `dotnet publish`, with no Dockerfile and no Docker on your machine. The network
policies went in before Postgres, so it was never reachable by other pods in the cluster. The migration ran as a Job
before any hub started. Nothing was running before, so there was nothing to drain.

## 7. Check it answers

In a second terminal:

```bash
kubectl -n orders-staging port-forward service/hub 8080
```

```text
Forwarding from 127.0.0.1:8080 -> 8080
Forwarding from [::1]:8080 -> 8080
```

In the first:

```bash
curl -s http://127.0.0.1:8080/trax/health; echo
curl -s -H 'Content-Type: application/json' -d '{"query":"{ __typename }"}' http://127.0.0.1:8080/trax/graphql; echo
```

```text
Healthy
{"data":{"__typename":"RootQuery"}}
```

The hub runs in Production here, so the template's demo keys and console are off and every train is refused until
you add real credentials ([Prepare a template for deployment](/docs/reference/prepare-a-template-for-deployment)).

Look at what the hub's pod was given:

```bash
kubectl -n orders-staging get pod -l app.kubernetes.io/name=hub \
  -o jsonpath='{.items[0].spec.hostUsers} {.items[0].spec.automountServiceAccountToken} {.items[0].spec.containers[0].securityContext.readOnlyRootFilesystem}{"\n"}'
```

```text
false false true
```

A user namespace of its own, no token for the API server, and a root filesystem it cannot write. Stop the
port-forward with Ctrl-C.

## 8. Release again while it serves

Make a change and commit it. The template's schedule runs every 20 seconds; make it 30:

```bash
sed -i 's/Every.Seconds(20)/Every.Seconds(30)/' Program.cs
git commit -qam "Run hello-world every 30 seconds"
```

Start a probe inside the namespace that asks the hub's Service for `/trax/health` five times a second. It is
labelled as one of the app's pods, which the namespace's network policy admits; a port-forward would not do, since
it follows one pod and breaks when that pod goes.

```bash
kubectl -n orders-staging run hub-probe --restart=Never --labels=trax/app=orders \
  --image=docker.io/library/busybox:1.37@sha256:a7dbc319b1438115a207e11930dc4bb3e643dcb3ec0bdd694bb36cd8fad289ec \
  --overrides='{"spec":{"securityContext":{"runAsNonRoot":true,"runAsUser":65534,"seccompProfile":{"type":"RuntimeDefault"}},"containers":[{"name":"hub-probe","image":"docker.io/library/busybox:1.37@sha256:a7dbc319b1438115a207e11930dc4bb3e643dcb3ec0bdd694bb36cd8fad289ec","command":["sh","-c","while true; do if wget -q -T 2 -O /dev/null http://hub:8080/trax/health; then echo ok; else echo fail; fi; sleep 0.2; done"],"securityContext":{"allowPrivilegeEscalation":false,"capabilities":{"drop":["ALL"]}}}]}}'
kubectl -n orders-staging wait --for=condition=Ready pod/hub-probe --timeout=60s
```

```text
pod/hub-probe created
pod/hub-probe condition met
```

Deploy:

```bash
trax deploy --env staging
```

```text
Asking hub what it needs (Orders.csproj)...
Deploying orders to namespace orders-staging in context default.
Pushing images tagged 20261124-152140-8b06e5d41c7a to localhost:5001...
  hub -> localhost:5001/orders-hub:20261124-152140-8b06e5d41c7a
Waiting for database...
Migrating the database (job orders-migrate-20261124-152140-8b06e5d41c7a)...
Draining: no host starts new work while the deploy waits up to 00:10:00 for what is busy (job orders-drain-20261124-152140-8b06e5d41c7a)...
Rolling the services onto 20261124-152140-8b06e5d41c7a...
Waiting for hub...
Ending the drain (job orders-resume-20261124-152140-8b06e5d41c7a)...
Deployed orders to staging. It is not exposed: reach a hub with 'kubectl port-forward -n orders-staging service/<hub> 8080'.
```

This time a hub was running, so the deploy drained before it rolled. The drain Job ran the new image once: it told
every host to start no new work, waited for what was busy, and exited. Its log says so:

```bash
kubectl -n orders-staging logs job/orders-drain-20261124-152140-8b06e5d41c7a | grep -E 'Drained|Nothing is busy'
```

```text
      Drained: no host starts new work until 2026-11-24 15:42:03Z. Waiting up to 00:10:00 for what is busy.
      Nothing is busy; the hosts can be replaced.
```

Then the new hub started beside the old one, took traffic once it was ready, and the old one stopped. What the
probe saw:

```bash
kubectl -n orders-staging logs hub-probe | sort | uniq -c
```

```text
    347 ok
```

Your count depends on how long the deploy took. There is no `fail` line. Delete the probe:

```bash
kubectl -n orders-staging delete pod hub-probe
```

If a run had still been going after ten minutes, the deploy would have stopped there, rolled nothing, and listed the
run. [Drain before a deploy](/docs/reference/drain-before-a-deploy) covers that case.

## 9. Release something broken

Give the hub a setting it cannot start with. In `trax.deploy.json`, change `10.42.0.0/16` to `not-a-network`, and
deploy:

```bash
sed -i 's#10.42.0.0/16#not-a-network#' trax.deploy.json
trax deploy --env staging
```

The migration and the drain run as before, since neither starts the web server. The new hub does, and fails. With
`maxUnavailable: 0` the old hub keeps serving the whole time. After five minutes with no progress the cluster marks
the rollout failed, and trax puts the hub back:

```text
Asking hub what it needs (Orders.csproj)...
Deploying orders to namespace orders-staging in context default.
Pushing images tagged 20261124-153310-8b06e5d41c7a-dirty to localhost:5001...
  hub -> localhost:5001/orders-hub:20261124-153310-8b06e5d41c7a-dirty
Waiting for database...
Migrating the database (job orders-migrate-20261124-153310-8b06e5d41c7a-dirty)...
Draining: no host starts new work while the deploy waits up to 00:10:00 for what is busy (job orders-drain-20261124-153310-8b06e5d41c7a-dirty)...
Rolling the services onto 20261124-153310-8b06e5d41c7a-dirty...
Waiting for hub...
Rolling the services back to the version before this deploy...
Ending the drain (job orders-resume-20261124-153310-8b06e5d41c7a-dirty)...
Deployment hub did not roll out: past its progress deadline (ReplicaSet "hub-6f9d8c7b54" has timed out progressing.). See 'kubectl describe deployment hub -n orders-staging'. Every service was rolled back to the version it ran before this deploy; the migration stays.
```

The command exits 1. The hub is the one step 8 deployed:

```bash
trax deploy status --env staging
```

```text
orders-staging in context default:
  hub             1/1 ready  localhost:5001/orders-hub:20261124-152140-8b06e5d41c7a
  database        1/1 ready  postgres:17
```

Put the file back:

```bash
git checkout trax.deploy.json
```

## Take it down

```bash
trax deploy destroy --env staging
```

It asks you to type `orders-staging`, deletes the app's Deployments, StatefulSet, Jobs, Services and network
policies, and keeps the namespace, the Secret and Postgres's volume, which the next deploy uses again.
`--delete-data` deletes the volume too. Deleting the VM deletes everything.

## What you have

A hub that runs with every capability dropped, in a user namespace of its own, with a root it cannot write,
reachable only from the app's own pods; a release that drains and rolls with no failed request; and a broken release
that put itself back.

From here:

- Reach it from your devices instead of `port-forward`: `"exposure": "tailnet"`
  ([Exposed on a tailnet](/docs/reference/cli#exposed-on-a-tailnet)).
- Limit what the hub may reach to a list of domains: `"egress"` on the service ([Egress](/docs/reference/cli#egress)).
- Use a database you run and back up, under the `production` profile
  ([`--target kubernetes`](/docs/reference/cli#--target-kubernetes)).
- A hub with workers: the [DistributedWorkers sample](https://github.com/TraxSharp/Trax/tree/main/Trax.Samples/samples/DistributedWorkers).
- What each layer protects against, and what has been proved: [Deployment security](/docs/security/deployment-security).

## SDK Reference

> [DeployManifest](/docs/sdk-reference/configuration/deploy-manifest) | [TraxMigrate](/docs/sdk-reference/configuration/trax-migrate) | [TraxDrain](/docs/sdk-reference/configuration/trax-drain)
