---
title: How a deploy works
description: "How trax deploy builds a deployment from the host's report and trax.deploy.json, what each target can and cannot do, and the order a cluster deploy runs in."
type: explanation
area: tooling
nav_order: 6
---

# How a deploy works

`trax deploy` takes the same app to one host, a Kubernetes cluster or AWS, from one file. This page is about how it
decides what to build, what each target gives you and what it refuses, and why a cluster deploy runs in the order it
does. The steps are in [A zero-downtime deploy on k3s](/docs/reference/a-zero-downtime-deploy-on-k3s) and
[Deploy a Trax app](/docs/reference/deploy-a-trax-app); every key and every refusal is in
[the CLI reference](/docs/reference/cli#deployments-trax-deploy).

## Two sources: the file and the host

What a deployment needs comes from two places, and neither can say what the other says.

**The host reports what its code needs.** Before anything is built, trax runs each project once with
`--trax-export-deploy`, in Production, and reads the [DeployManifest](/docs/sdk-reference/configuration/deploy-manifest)
it writes: its roles (`api`, `scheduler`, `worker`, `job-runner`), its database provider, whether it connects to
RabbitMQ, the paths of its GraphQL, health and console endpoints, the authentication schemes it registers, and
where it reads secrets from. The host runs its own startup checks first, so a host that would refuse to start in
Production refuses the deploy, before anything is written.

**`trax.deploy.json` holds the choices only you can make.** Which projects are services and what they are called,
how many copies, how much memory and CPU, the names of the settings and secrets the deployment must supply, which
domains a service may reach, and per environment: the target, the profile, the registry, the database, the
exposure and the drain's timeout.

What follows from the report is never written in the file:

| The host reports | So the deployment |
|---|---|
| It serves HTTP and runs `api` or `scheduler` | Treats it as a hub: one copy, a Service in front of it, overlap during a roll, a loopback port under compose. |
| It connects to RabbitMQ | Runs RabbitMQ beside it, or refuses where it cannot (the `production` profile, EKS, ECS). |
| A health endpoint | Probes it on a cluster, and routes the load balancer's health check to it on ECS. |
| It keeps runs anywhere but Postgres | Refuses. Every target keeps runs in Postgres. |
| Tailnet sign-in (`AddTraxTailscaleAuth`) | Refuses it anywhere but the hub an environment exposes on a tailnet. |
| No `aws-secrets` source | Refuses it on EKS, where Secrets Manager is the only way its secrets reach it. |

The reason is drift. A file that said "this host needs RabbitMQ" or "this host is the hub" would be right on the day
it was written and wrong after the next change to the code. The code is what runs, so the code is asked. The one
thing the file may never set is `Trax__Deploy__*`: that is how the deployment tells the host facts about where it
is, such as that it sits behind the tailnet proxy, and an app must not claim them for itself.

## What each target does

There are three targets, and the Kubernetes one runs three ways:

| | compose | kubernetes, `runtime: podman` | kubernetes, a cluster | kubernetes on EKS | aws (ECS Fargate) |
|---|---|---|---|---|---|
| Who runs it | trax writes `compose.yaml`; you run `compose up` | trax writes Quadlet units; systemd runs them | trax applies it | trax applies it, and the AWS foundation | trax applies two CloudFormation stacks |
| A hub's release | Down while its container is replaced | Down while its unit restarts | No outage: the new copy starts beside the old | As a cluster | No outage: old and new tasks overlap |
| A release that does not start | Left as it is | Left as it is | Every service rolled back | As a cluster | Rolled back by ECS's circuit breaker |
| Drain before replacing | A `drain` service | A drain pod, chained before the restart | A drain Job | As a cluster | A drain task |
| User namespace | `userns_mode: nomap` (podman only) | `UserNS=nomap` | `hostUsers: false` | `hostUsers: false` | None that trax sets |
| Who reaches a hub | `127.0.0.1` | `127.0.0.1`, or nothing with a tailnet | The app's own pods (NetworkPolicy) | As a cluster | The load balancer |
| `egress` | A sidecar; one copy per held service | A sidecar | Init container, native sidecar, admission policy | As a cluster, plus RDS, Pod Identity and the foundation's Secrets Manager endpoint | Refused |
| `"exposure": "tailnet"` | Refused | Tailscale and the edge in a pod of their own beside the hub | The Tailscale operator's Ingress and an edge | As a cluster | Refused |
| Postgres | In the stack | In the stack (`try`) or a Secret | A StatefulSet (`try`) or a Secret | RDS, from the foundation | RDS, from the foundation |
| RabbitMQ | In the stack | In the stack (`try`) | A Deployment (`try`) | Refused | Refused |
| `pids` | Applied | Refused | Refused | Refused | Refused |

Some of this is a choice and some is what the platform allows. compose and the podman runtime run on one host
whose container engine stops a container before it starts its replacement, so a hub there is down for that moment;
for no outage on one machine, run k3s in a virtual machine
([Deploy to one machine with no downtime](/docs/reference/deploy-to-one-machine-with-no-downtime)). ECS is the
cheapest way onto AWS for a small app, and the foundation it shares with EKS means moving between them does not move
the database.

The repository's deploy proof, `.github/ci/deploy/`, runs the kubernetes target on k3s and the podman runtime under
systemd. It does not run ECS, EKS or any managed cluster; see
[what has been proved](/docs/security/deployment-security#what-has-been-proved).

## The order of a cluster deploy

1. Ask each host what it needs.
2. Check the namespace is this app's and its Secret has every key. A missing key stops the deploy here, with
   nothing made.
3. Create the namespace when it is missing.
4. Build and push the images.
5. Apply the egress admission policy, when a service names egress.
6. Apply the network policies. With a tailnet exposure, prove the network plugin enforces them: a probe from one of
   the app's pods must reach a control listener the policies admit it to and must not reach a listener labelled as
   the edge. If it reaches the edge's, or never reaches the control, the deploy stops with nothing exposed.
7. Bring up Postgres and RabbitMQ in the cluster, on the `try` profile.
8. Run the migrate Job. If it fails, the deploy stops: no service has moved.
9. If a service ran before, run the drain Job. If something is still busy at the timeout, the deploy stops, nothing
    has moved, and the drain is ended. A first deploy skips this step.
10. Write a new tailnet proxy secret, when the environment is exposed on a tailnet. It comes after the drain so a
    refused deploy leaves the Secret holding the value the running hub and edge were started with.
11. Apply the services and wait for each to roll out. If one does not, put every service back as it was.
12. After a drain, run the resume Job, whether the roll succeeded or was rolled back.

Each step is where it is for a reason:

- **The Secret is checked before anything is applied**, even the namespace, so a deploy that is missing a key leaves
  nothing half made. trax never writes the app's secrets; it only reads that they are there. So is ownership: a
  namespace or an object another app deployed, or one trax would apply over that carries no `trax/app` label, is
  refused.
- **The admission policy comes before any pod.** When a service is held to its egress, one init container per pod
  needs `NET_ADMIN`, and the policy is what refuses it to every other container in the namespace. A pod created
  before it would not have been checked.
- **The network policies come before Postgres and RabbitMQ**, so neither is ever up while other pods in the cluster
  can reach it.
- **The migration runs before any host moves**, as a Job on the new image. The version before it keeps serving while
  it runs, so a migration has to leave that version working; for the same reason a rollback does not undo it. If it
  fails, the deploy stops with the end of its log.
- **The tailnet proxy secret is minted after the drain and before the services**, so the hub and the edge
  restart onto the new value in the same roll, and a deploy refused before the roll leaves the value they run with.
  Under podman the new value is written beside the old one and moved into place by the printed restart step. For those seconds a signed-in request is anonymous; see
  [Tailnet sign-in](/docs/security/tailnet-sign-in).
- **The drain comes after the migration and before the roll.** A hub that starts fails every run still InProgress
  from before it started, on any host, because nothing records which host owns a run. The drain makes sure there is
  nothing live for it to fail: every host stops starting new work, and the Job waits up to `drainTimeout` for what is
  busy. Past it, the deploy refuses and lists what was busy. A first deploy has nothing to drain.
  [Drain before a deploy](/docs/reference/drain-before-a-deploy) covers it.
- **The roll.** A hub runs one copy and rolls with `maxSurge: 1` and `maxUnavailable: 0`: the new copy starts beside
  the old one, and the old one stops only once the new one is ready. It sleeps ten seconds in `preStop`, so the
  Service has stopped routing to it before it is signalled. Workers roll at their count.
- **A rollback** happens when a service does not roll out: a pod that can never start, or a Deployment that makes no
  progress for five minutes (`progressDeadlineSeconds: 300`). Every Deployment that existed gets its earlier spec
  back, and one this deploy created is deleted, as ECS's circuit breaker does. A first deploy has nothing to go back
  to. On a tailnet exposure the proxy secret this deploy replaced is written back first, since the old hub still
  holds it and the edge put back beside it reads the Secret as it starts.
- **The resume Job** ends the drain, after a rollback too. A hub that starts leaves it in place, so only the resume,
  Resume on the console's scheduler page or the lapse ends it. A resume that fails does not undo the deploy, and a
  drain a deploy left behind (Ctrl-C, a crash) lapses on its own. The drain sets it to lapse, once it has finished
  waiting, after the time the roll and a rollback can take: ten minutes per Deployment for each, plus ten.

The drain, the migration and the resume reach the database from inside the cluster, through the app's own image.
Nothing outside the cluster has to reach the app to deploy it.

## One hub, and two during a roll

In steady state a hub runs one copy. A second copy that restarted on its own, with no drain before it, would run its
startup recovery and fail the runs live on the first. During a roll the drain has already made sure nothing is live,
and dispatch copes with two hubs (work is claimed with `FOR UPDATE SKIP LOCKED`, the manifest manager holds an
advisory lock), so the old and new hub overlap for the length of the roll and the API stays up. The trade-off is
recorded in `Trax.Cli/docs/adr/0007`.

## What is refused, and why

A field a target cannot honour is refused when the file is read, naming the field, rather than accepted and ignored.
A limit that is silently dropped is worse than none, because you believe it is there.

| Refused | Where | Why |
|---|---|---|
| `pids` | kubernetes, aws | Neither has a per-container process limit; on a cluster it is the node's `podPidsLimit`. |
| `limits` | aws | RDS is sized by the profile, and there is no broker to limit. |
| `egress` | aws | Fargate gives a task no `NET_ADMIN`, so the rules cannot be applied, and a security group filters by address, not by name. Use the kubernetes target on EKS. |
| `exposure` | compose, aws | compose publishes on loopback and AWS serves through its own load balancer. |
| `"exposure": "public"` | kubernetes | Not offered yet. |
| Postgres or RabbitMQ in the cluster | `production` profile | One copy with no backups and no failover. Name a database you run. |
| RabbitMQ | EKS, ECS | trax cannot name an existing broker yet. |
| `count` over 1 on a held service | compose | Its copies would share the sidecar's network namespace and its ports. |
| More than one API service, or more than one copy of the API or the scheduler | aws | The ECS stack serves one hub. |

## Why not kind

A kind node is a container, and a pod on it cannot have a user namespace of its own. Every Trax pod asks for one
(`hostUsers: false`), so on kind no Trax pod starts. There is no field to turn it off: it is what keeps root in a
container from being root on the node. Rather than wait out the rollout, `trax deploy` reads the node's failed
attempts to create the pod and stops, saying the node cannot give it a user namespace. For a cluster on one
machine, run k3s in a virtual machine.

## Why Kata means a cluster

A [sandboxed train](/docs/scheduler/run-a-train-in-a-sandbox) runs each run in a Kata microVM, with its own guest
kernel. Kata Containers 4 has no OCI command line, so podman cannot run it at all. One machine with Kata is
therefore k3s with a Kata RuntimeClass and the launcher's Kubernetes backend, the same as any cluster; the podman
backend is only ever as isolating as gVisor. `trax deploy` does not deploy the launcher: the launcher image writes
its own namespace's objects (`--trax-sandbox-manifests`), from the configuration it will run with, and a cluster
admin applies them. The [sandbox threat model](/docs/security/sandbox-threat-model) says what it is kept from.

## What it does not do

- **Create a cluster**, on any cloud. It applies to the current kubeconfig context, and on EKS only after EKS says
  that context's server is the named cluster's.
- **Install the Tailscale operator, or write a Tailscale or app credential.** You create the namespace's Secret; the
  operator holds its own credentials.
- **Back up the in-cluster Postgres.** It is for a `try` environment you can lose.
- **Make one machine more than one machine.** A host that goes down takes the app with it, cluster or not.

## SDK Reference

> [DeployManifest](/docs/sdk-reference/configuration/deploy-manifest) | [TraxMigrate](/docs/sdk-reference/configuration/trax-migrate) | [TraxDrain](/docs/sdk-reference/configuration/trax-drain) | [AddTraxTailscaleAuth](/docs/sdk-reference/api-auth/add-trax-tailscale-auth) | [AddTraxAwsSecretsAsync](/docs/sdk-reference/configuration/add-trax-aws-secrets)
