---
title: Deploy a Trax app
description: "Deploy a Trax app with trax deploy: a compose stack for any host, an AWS environment, or a Kubernetes namespace, built from what each host reports it needs."
type: how-to
area: tooling
nav_order: 6
---

# Deploy a Trax app

`trax deploy` deploys the Trax hosts in a solution: as a compose stack for any machine that runs rootless podman,
as an AWS environment, or into a namespace of a Kubernetes cluster. You name the projects and the choices only you can make in `trax.deploy.json`; each host
reports the rest itself when trax runs it once with `--trax-export-deploy`: its database, whether it serves the API
or runs the scheduler, its endpoints and its sign-in. The [CLI reference](/docs/reference/cli#deployments-trax-deploy)
has every option.

Before you start, the hosts end with `await app.RunTraxAsync(args);` in place of `app.Run()`, as the templates do,
and keep their runs in Postgres when `ConnectionStrings:Trax` is set
([Switch a template to Postgres](/docs/reference/switch-a-template-to-postgres)). An API host maps a health check
(`app.MapHealthChecks("/trax/health")`).

## 1. Describe the deployment

```bash
trax deploy init --app orders            # compose only
trax deploy init --app orders --target aws --region us-west-2 --domain orders.example.com
trax deploy init --app orders --target kubernetes
```

`init` makes a service of every Trax host project under the directory. List the configuration the hosts read
under `settings` (an issuer, an origin) and `secrets` (an API key, a password), as environment names with `__` for
`:`. A deploy refuses to go ahead while one has no value. Each environment under `environments` names its
`target` (`compose`, `aws` or `kubernetes`), and `trax deploy --env <name>` deploys it there.

## 2. Run it on one machine

```bash
trax deploy --target compose --build
cp .env.example .env                      # fill in every value; never commit it
podman compose up -d
```

The stack is Postgres, RabbitMQ when a host uses it, one `migrate` run of the new version, then each service. The
API service answers on port 8080. Every service drops all Linux capabilities (Postgres and RabbitMQ get back the
few their entrypoints need), runs in a user namespace that leaves your own uid out of it (`userns_mode: nomap`,
which docker does not accept), and is held to 512 processes and a memory limit: a service's `memory` in MiB,
1024 when it names none.

Passwords reach the hosts as values of their own, never inside a connection string or a URI: Postgres's as
`PGPASSWORD`, RabbitMQ's as `RabbitMq__Password` beside `RabbitMq__Host` and `RabbitMq__UserName`. A host that
broadcasts through RabbitMQ reads them with `UseRabbitMq(builder.Configuration)`.

For docker compose, add an environment `{ "target": "compose", "engine": "docker" }` and run
`trax deploy --env <name>`: the same stack without `userns_mode`, which docker refuses.

## 3. Deploy to AWS

An AWS stack keeps runs in Postgres and does not provide RabbitMQ yet, so a deploy refuses a host that keeps its
runs anywhere else or broadcasts through RabbitMQ (`UseRabbitMq`). It also refuses more than
one API service, an API with no health check, and more than one copy of the API or the scheduler. Deploy those with
`--target compose`.

Sign in to the account the usual way (`aws sso login`, a profile, or a role in CI), then:

```bash
trax deploy plan --env production        # read .trax/deploy/aws/orders-production.template.json and the cost estimate
trax deploy secret set Stripe__ApiKey --env production
trax deploy --env production
trax deploy status --env production
```

The first deploy takes 15 to 25 minutes, most of it the database and, in production, the certificate. Later
deploys push new images, migrate, and roll the services. With `"auth": { "kind": "cognito" }`, the user pool trax
creates admits only users you invite; add the first in the Cognito console, and put operators in its `Operator`
group.

Use the `try` profile for an environment you will throw away: no domain, no NAT gateway, a small database deleted
with the stack. CloudFront serves it over HTTPS, but reaches the load balancer over plain HTTP inside AWS.

## 4. Deploy to Kubernetes

A Kubernetes environment goes into the cluster your current kubeconfig context names (`kubectl config
current-context`); trax never creates one. `init --target kubernetes` writes a `staging` environment on the `try`
profile with Postgres in the cluster:

```json
"staging": { "target": "kubernetes", "profile": "try", "database": { "inCluster": true } }
```

trax writes no secret into a manifest. The app's secrets, and `POSTGRES_PASSWORD` (and `RABBITMQ_PASSWORD` when a
host uses RabbitMQ) for what runs in the cluster, come from one Secret named `<app>-<env>` in the namespace, which
you create:

```bash
kubectl create namespace orders-staging
kubectl create secret generic orders-staging -n orders-staging \
  --from-literal=POSTGRES_PASSWORD="$(openssl rand -hex 24)" \
  --from-literal=Stripe__ApiKey=sk_test_...
trax deploy plan --env staging            # the manifests, under .trax/deploy/kubernetes/orders-staging/
trax deploy --env staging
trax deploy status --env staging
kubectl port-forward -n orders-staging service/hub 8080
```

A deploy refuses before it applies anything while the Secret lacks a key. It brings up Postgres (and RabbitMQ),
runs the migration as a Job, and only when that succeeds rolls each service and waits for it to be ready. The hub
runs one copy and stops before its next version starts; workers roll at their `count`. Nothing is exposed outside
the cluster yet, so reach the hub with `kubectl port-forward`.

The `production` profile, the default, refuses Postgres and RabbitMQ in the cluster: name an existing database
with `"database": { "secret": "orders-db", "key": "connection" }`, a Secret holding its whole connection string.
Without a `registry`, images are built into your local container runtime, which Docker Desktop's and podman's
clusters read; kind does not, so give it a registry (`"registry": "localhost:5001"`).

## One hub

The API and the scheduler run as one copy each. On AWS a deploy starts the new copy before it stops the old one, but runs
still live on the old copy when it stops can fail: a restarting host fails the runs it finds in progress, including
another host's. On Kubernetes the old copy stops before the new one starts, so a deploy has a few seconds with no
hub, never two. Workers scale freely (`count`). Running more than one hub waits on hosts holding leases on their
runs.

## Take it down

```bash
podman compose down                       # compose; add -v to drop the database too
trax deploy destroy --env production      # AWS; production's database leaves a final snapshot
trax deploy destroy --env staging         # Kubernetes; keeps the namespace, the Secret and the database's volume
```

In production, `destroy` keeps the Cognito user pool and its users, and the next deploy of the environment signs in
against it again. `destroy --delete-data` deletes the pool and every user in it.

## SDK Reference

> [TraxMigrate](/docs/sdk-reference/configuration/trax-migrate) | [DeployManifest](/docs/sdk-reference/configuration/deploy-manifest) | [UsePostgres](/docs/sdk-reference/configuration/use-postgres)
