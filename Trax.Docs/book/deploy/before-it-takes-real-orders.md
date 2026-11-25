---
title: "19.10 Before it takes real orders"
description: "Chapter 19 of the Trax book, section 10: the same trax.deploy.json on a Kubernetes cluster, a release that drains, one that is rolled back, and what is left."
type: tutorial
area: general
parent: "19. Deploy"
grand_parent: The Trax book
nav_order: 10
snippet_context: none
follows: book/deploy/the-console
---

# 19.10 Before it takes real orders

The compose stack is one machine, and its hub is down for the seconds a release replaces it. The
`cluster` environment in `trax.deploy.json` takes the same services to Kubernetes, where a release
has no outage and one that cannot start is put back.

## On a cluster

You need a cluster your kubeconfig's current context names, and a registry it pulls from. On one
machine, run k3s in a virtual machine with a registry on `localhost:5001`, as
[A zero-downtime deploy on k3s](/docs/reference/a-zero-downtime-deploy-on-k3s) sets up in its
step 3; this section ran on k3s v1.36.6 in a Debian 13 VM. kind does not work: a pod there cannot
have the user namespace every Trax pod asks for.

trax writes no secret into what it applies. The passwords and the signing key come from one Secret,
`shopfront-cluster`, which you create, through a file rather than the command line so they stay out
of your shell's history:

```bash
kubectl create namespace shopfront-cluster
cat > shopfront-cluster.env <<EOF
POSTGRES_PASSWORD=$(openssl rand -base64 24)
RABBITMQ_PASSWORD=$(openssl rand -base64 24)
Broadcast__SigningKey=$(openssl rand -base64 32)
EOF
kubectl create secret generic shopfront-cluster -n shopfront-cluster --from-env-file=shopfront-cluster.env
rm shopfront-cluster.env
```

The settings are in the environment itself, in `trax.deploy.json`. See what a deploy would apply:

```bash
dotnet trax deploy plan --env cluster
```

```text
Asking hub what it needs (Shopfront.Hub/Shopfront.Hub.csproj)...
Asking worker what it needs (Shopfront.Worker/Shopfront.Worker.csproj)...
Wrote /home/you/Shopfront/.trax/deploy/kubernetes/shopfront-cluster:
  Namespace                        shopfront-cluster
  ValidatingAdmissionPolicy        trax-egress-shopfront-cluster
  ValidatingAdmissionPolicyBinding trax-egress-shopfront-cluster
  NetworkPolicy                    shopfront-deny
  NetworkPolicy                    shopfront-within
  Service                          database
  StatefulSet                      database
  Service                          rabbitmq
  Deployment                       rabbitmq
  Job                              shopfront-migrate-20261125-164255
  Job                              shopfront-drain-20261125-164255
  Service                          hub
  Deployment                       hub
  Deployment                       worker
  Job                              shopfront-resume-20261125-164255
They read POSTGRES_PASSWORD, RABBITMQ_PASSWORD, Broadcast__SigningKey from the Secret shopfront-cluster in namespace shopfront-cluster, which trax does not write.
```

The admission policy keeps the capability the egress rules need to the one container that applies
them. The network policies let the app's pods reach each other and nothing else in the cluster reach
them. The migration, the drain and the resume are Jobs on the hub's new image, as they were
services under compose. Deploy it:

```bash
dotnet trax deploy --env cluster
```

```text
Asking hub what it needs (Shopfront.Hub/Shopfront.Hub.csproj)...
Asking worker what it needs (Shopfront.Worker/Shopfront.Worker.csproj)...
Deploying shopfront to namespace shopfront-cluster in context default.
Pushing images tagged 20261125-163325 to localhost:5001...
  hub -> localhost:5001/shopfront-hub:20261125-163325
  worker -> localhost:5001/shopfront-worker:20261125-163325
Waiting for database...
Waiting for rabbitmq...
Migrating the database (job shopfront-migrate-20261125-163325)...
Rolling the services onto 20261125-163325...
Waiting for hub...
Waiting for worker...
Deployed shopfront to cluster. It is not exposed: reach a hub with 'kubectl port-forward -n shopfront-cluster service/<hub> 8080'.
```

```bash
kubectl -n shopfront-cluster get pods
```

```text
NAME                                      READY   STATUS      RESTARTS   AGE
database-0                                1/1     Running     0          72s
hub-68799b8cff-xcjnl                      2/2     Running     0          16s
rabbitmq-7b498dd879-gs28h                 1/1     Running     0          72s
shopfront-migrate-20261125-163325-lwwz6   0/2     Completed   0          40s
worker-7df66688f6-bqfgg                   2/2     Running     0          16s
worker-7df66688f6-l698d                   2/2     Running     0          16s
```

Two workers, as `counts` says, each with an egress container of its own beside it (`2/2`). Each pod
runs with a user namespace of its own, a read-only root, its service's memory and CPU, and no token
for the cluster's API. Nothing outside the cluster reaches the hub; reach it through a port-forward,
with `$CAT` from 19.7:

```bash
kubectl -n shopfront-cluster port-forward service/hub 8080 &
curl -s http://localhost:8080/health/ready
curl -s http://localhost:8080/trax/graphql -H 'Content-Type: application/json' -d '{"id":"'$CAT'"}'
```

```text
Healthy
{"data":{"discover":{"products":{"nodes":[{"sku":"CAP","name":"Cap","price":18.50,"stock":5},{"sku":"MUG","name":"Mug","price":12.00,"stock":10},{"sku":"TEE","name":"T-shirt","price":25.00,"stock":8}]}}}}
```

### A release

`dotnet trax deploy --env cluster` again releases a new version. It migrates, then drains, as the
compose stack did. With a restock still running, the drain Job waits, and the hub and the workers
keep their old images until it ends:

```text
Migrating the database (job shopfront-migrate-20261125-163506)...
Draining: no host starts new work while the deploy waits up to 00:05:00 for what is busy (job shopfront-drain-20261125-163506)...
Rolling the services onto 20261125-163506...
Waiting for hub...
Waiting for worker...
Ending the drain (job shopfront-resume-20261125-163506)...
Deployed shopfront to cluster. It is not exposed: reach a hub with 'kubectl port-forward -n shopfront-cluster service/<hub> 8080'.
```

The drain Job's log (`kubectl -n shopfront-cluster logs job/shopfront-drain-<tag>`) says what it is
waiting for, as the compose stack's did:

```text
      1 still busy, 00:04:52.1473004 left before the timeout.
```

Then the hub rolls with no outage: its new copy starts beside the old one, takes the Service's
traffic once it is ready, and only then does the old one stop. Two hub pods ran at once during this
release. Outside a release the hub runs one copy, since a second hub starting on its own would fail
the first one's runs; the drain is what makes the overlap safe.

### A release that cannot start

A release whose hub never gets ready is put back. Give the hub a Kestrel endpoint that is not a URL
in `Shopfront.Hub/appsettings.json`, which only serving reads, so the migration and the drain pass:

```json
"Kestrel": { "Endpoints": { "Http": { "Url": "not a url" } } }
```

```text
Rolling the services onto 20261125-163632...
Waiting for hub...
Rolling the services back to the version before this deploy...
Ending the drain (job shopfront-resume-20261125-163632)...
Deployment hub did not roll out: past its progress deadline (ReplicaSet "hub-89979c4bb" has timed out progressing.). See 'kubectl describe deployment hub -n shopfront-cluster'. Every service was rolled back to the version it ran before this deploy; the migration stays.
```

The old hub served throughout, and the workers, which had rolled onto the new image, are back on the
one before it, as `dotnet trax deploy status --env cluster` shows:

```text
shopfront-cluster in context default:
  hub             1/1 ready  localhost:5001/shopfront-hub:20261125-163506
  rabbitmq        1/1 ready  rabbitmq:4
  worker          2/2 ready  localhost:5001/shopfront-worker:20261125-163506
  database        1/1 ready  postgres:17
```

The deploy waits five minutes for a Deployment that makes no progress before it gives up. Take the
endpoint out again.

### Staff only, on a tailnet

`"exposure": "tailnet"` on an environment puts the hub on your tailnet through the
[Tailscale Kubernetes operator](https://tailscale.com/kb/1236/kubernetes-operator), at
`https://shopfront-<env>.<tailnet>.ts.net`, and nowhere else. It suits a deployment only staff use,
and closes 19.9's question for the console, since only your tailnet's devices reach it. Shopfront's
storefront calls the hub from customers' browsers, and a public exposure is not offered yet, so for
the shop itself the hub needs an ingress or a load balancer in front of it, which decides who
reaches it.

## And before real orders

- **HTTPS** in front of the hub, terminated at the ingress or the load balancer.
- **Secrets** from a secret store, not a file on a laptop.
- **A database you run and back up.** The `try` profile runs Postgres and RabbitMQ in the cluster,
  one copy each, with no backups. The `production` profile refuses that: it names an existing
  database with `"database": { "secret": "...", "key": "connection" }`.
- **Fakes.** The payment provider, the courier, the supplier and the warehouse are fakes, registered
  in `AddShopfront`. Each is an interface, so a real one replaces it there, and nothing else
  changes. A real one calls out, so its domain goes in that service's `egress`, or the call fails.
- **A second hub** needs persisted-operation changes broadcast between hubs: replace `SingleNode()`
  with `UseRabbitMqInvalidation(builder.Configuration, o => o.SigningKey = broadcastKey)`. Only one
  hub runs the schedule at a time; the others serve. `trax deploy` runs one hub until hosts hold
  leases on their runs.

## What we have

Shopfront, deployed with `trax deploy`: a hub that serves the storefront and the console, workers
that run the jobs, the two sharing one Postgres and one RabbitMQ, every process held to what it may
reach, and a release that drains first. The same file runs it on one machine and on a cluster. That
is the end of the book.

## SDK Reference

> [DeployManifest](/docs/sdk-reference/configuration/deploy-manifest) | [UseRabbitMq](/docs/sdk-reference/configuration/use-rabbit-mq#reading-the-broker-from-configuration) | [TraxDrain](/docs/sdk-reference/configuration/trax-drain) | [How a deploy works](/docs/reference/how-a-deploy-works) | [A zero-downtime deploy on k3s](/docs/reference/a-zero-downtime-deploy-on-k3s)

---

That is the end of the book. [Back to the contents](/docs/book).
