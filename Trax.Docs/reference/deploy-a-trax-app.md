---
title: Deploy a Trax app
description: "Deploy a Trax app with trax deploy: a compose stack for any host, an AWS environment, or a Kubernetes namespace, built from what each host reports it needs."
type: how-to
area: tooling
nav_order: 6
---

# Deploy a Trax app

`trax deploy` deploys the Trax hosts in a solution: as a compose stack for any machine that runs rootless podman or docker,
as an AWS environment, or into a namespace of a Kubernetes cluster. You name the projects and the choices only you can make in `trax.deploy.json`; each host
reports the rest itself when trax runs it once with `--trax-export-deploy`: its database, whether it serves the API
or runs the scheduler, its endpoints and its sign-in. The [CLI reference](/docs/reference/cli#deployments-trax-deploy)
has every option.

New to it? [A zero-downtime deploy on k3s](/docs/reference/a-zero-downtime-deploy-on-k3s) walks one path end to end.
[How a deploy works](/docs/reference/how-a-deploy-works) explains what each target can and cannot do and the order a
deploy runs in, and [Deployment security](/docs/security/deployment-security) what each layer keeps out.

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
podman compose up -d migrate drain && podman compose wait drain && podman compose up -d
```

The same three commands release every later version: the migration and the drain run while the old containers still
serve, and only then are the services replaced. A plain `podman compose up -d` would stop the old containers first.

The stack is Postgres, RabbitMQ when a host uses it, one `migrate` run of the new version, then each service. The
API service answers on port 8080. Every service drops all Linux capabilities (Postgres and RabbitMQ get back the
few their entrypoints need), runs in a user namespace that leaves your own uid out of it (`userns_mode: nomap`,
which docker does not accept), and is held to 512 processes. A Trax service's root filesystem is read-only, with a
scratch `/tmp`. A service that names `memory` (MiB) or `cpus` runs under them; one that names neither has no limit,
so set them in production.

Passwords reach the hosts as values of their own, never inside a connection string or a URI: Postgres's as
`PGPASSWORD`, RabbitMQ's as `RabbitMq__Password` beside `RabbitMq__Host` and `RabbitMq__UserName`. A host that
broadcasts through RabbitMQ reads them with `UseRabbitMq(builder.Configuration)`.

For docker compose, add an environment `{ "target": "compose", "engine": "docker" }` and run
`trax deploy --env <name>`: the same stack without `userns_mode`, which docker refuses.

## 3. Deploy to AWS

An AWS stack keeps runs in Postgres and does not provide RabbitMQ yet, so a deploy refuses a host that keeps its
runs anywhere else or broadcasts through RabbitMQ (`UseRabbitMq`). It also refuses more than
one API service, an API with no health check, and more than one copy of the API or the scheduler. Deploy those with
`--target compose`, or to Kubernetes, whose `try` profile runs RabbitMQ in the cluster.

Sign in to the account the usual way (`aws sso login`, a profile, or a role in CI), then:

```bash
trax deploy plan --env production        # read .trax/deploy/aws/orders-production{-base,}.template.json and the cost estimate
trax deploy secret set Stripe__ApiKey --env production
trax deploy --env production
trax deploy status --env production
```

An environment is two CloudFormation stacks: `orders-production-base` holds the network, the database, its secret
and the sign-in, and `orders-production` runs the services on ECS over it. The first deploy takes 15 to 25
minutes, most of it the database and, in production, the certificate. Later deploys push new images, migrate, drain the
running hosts so nothing is cut off mid-run, and roll the services. With `"auth": { "kind": "cognito" }`, the user pool trax
creates admits only users you invite; add the first in the Cognito console, and put operators in its `Operator`
group.

Use the `try` profile for an environment you will throw away: no domain, no NAT gateway, a small database deleted
with the environment's foundation. CloudFront serves it over HTTPS, but reaches the load balancer over plain HTTP inside AWS.

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
printf 'POSTGRES_PASSWORD=%s\n' "$(openssl rand -hex 24)" > orders-staging.env
$EDITOR orders-staging.env                # add Stripe__ApiKey=..., one KEY=value line each
kubectl create secret generic orders-staging -n orders-staging --from-env-file=orders-staging.env
rm orders-staging.env
trax deploy plan --env staging            # the manifests, under .trax/deploy/kubernetes/orders-staging/
trax deploy --env staging
trax deploy status --env staging
kubectl port-forward -n orders-staging service/hub 8080
```

The values go through a file rather than `--from-literal`, which would leave them in your shell's history and in the
process list. A deploy refuses before it applies anything, even the namespace, while the Secret lacks a key. It brings up Postgres (and RabbitMQ),
runs the migration as a Job, and only when that succeeds drains the hosts already running (they stop starting new
work, and the deploy waits up to the environment's `drainTimeout`, ten minutes by default, for what is busy), rolls
each service and waits for it to be ready, then ends the drain. The hub runs one copy, and its next version starts
beside it and takes traffic before the old one stops; workers roll at their `count`. Work still busy at the timeout
refuses the deploy with a list of it; `--force` rolls anyway. See
[Drain before a rollout](/docs/reference/cli#drain-before-a-rollout). Every pod in the namespace accepts connections
only from the app's own pods. Nothing outside the cluster reaches the hub, so reach it with `kubectl port-forward`.

To reach it from your devices instead, put it on your tailnet: install the
[Tailscale Kubernetes operator](https://tailscale.com/kb/1236/kubernetes-operator) in the cluster and add
`"exposure": "tailnet"` to the environment. The hub is then at `https://<app>-<env>.<tailnet>.ts.net`, and nothing
binds a public address. With it the hub can sign people in by their tailnet login
([`AddTraxTailscaleAuth`](/docs/sdk-reference/api-auth/add-trax-tailscale-auth)); the
[CLI reference](/docs/reference/cli#exposed-on-a-tailnet) says what it renders.

The `production` profile, the default, refuses Postgres and RabbitMQ in the cluster: name an existing database
with `"database": { "secret": "orders-db", "key": "connection" }`, a Secret holding its whole connection string.
Without a `registry`, images are built into your local container runtime, for a cluster whose nodes read it. kind is
not supported: its nodes are containers, so a pod there cannot have the user namespace every Trax pod asks for. On one
machine, run k3s in a virtual machine ([Deploy to one machine with no downtime](/docs/reference/deploy-to-one-machine-with-no-downtime)).

On an EKS cluster you created, an `"aws"` block names it, and trax deploys the AWS foundation beside it for the
database and secrets: the pods read their secrets from Secrets Manager with a role of their own, through
`AddTraxAwsSecretsAsync`, so none becomes a Kubernetes Secret. The
[CLI reference](/docs/reference/cli#on-eks) has the keys and what a deploy checks first.

The same environment runs on one host with no cluster: give it `"runtime": "podman"` and `trax deploy --env` writes
pod YAML and Quadlet units for systemd to run, takes the secret values from the `.env` beside `trax.deploy.json` into
a file only you can read under `~/.config/trax/`, and prints the commands that install and start the units. With
`"exposure": "tailnet"` a pod beside the hub's runs Tailscale, which reads its auth key from `TAILSCALE_AUTH_KEY` in
the same `.env`, and no port is published on the host. The
[CLI reference](/docs/reference/cli#on-one-host-under-podman) lists what differs from a cluster.

## One hub

The API and the scheduler run as one copy each. On Kubernetes and AWS a deploy drains the hosts first, then starts the
hub's new copy beside the old one and stops the old one once the new one is ready, so the API stays up and the new
hub's startup finds no live run to fail. Under compose and the podman runtime the hub stops before its new version
starts. Workers scale freely (`count`). In steady state a hub stays one copy: a second copy that restarted with no
drain before it would fail the runs live on the first. Running more than one hub waits on hosts holding leases on
their runs.

## Take it down

```bash
podman compose down                       # compose; add -v to drop the database too
trax deploy destroy --env production      # AWS; production's database leaves a final snapshot
trax deploy destroy --env staging         # Kubernetes; keeps the namespace, the Secret and the database's volume
```

On AWS, `destroy` deletes the services' stack, then the foundation. It refuses, before deleting anything, when the
foundation's network holds something trax did not create there, such as an EKS cluster.

An environment deployed by a trax from before the split is one stack. A deploy refuses it; destroy it and deploy
again. The new database starts empty. Production keeps a final snapshot of the old one, which you restore into the
new database by hand, and keeps its user pool, so its users still sign in. A `try` environment loses its data and its
users.

In production, `destroy` keeps the Cognito user pool and its users, and the next deploy of the environment signs in
against it again. `destroy --delete-data` deletes the pool and every user in it.

## SDK Reference

> [TraxMigrate](/docs/sdk-reference/configuration/trax-migrate) | [DeployManifest](/docs/sdk-reference/configuration/deploy-manifest) | [UsePostgres](/docs/sdk-reference/configuration/use-postgres)
