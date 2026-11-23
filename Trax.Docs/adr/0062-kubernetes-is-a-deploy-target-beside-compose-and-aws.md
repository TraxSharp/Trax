---
authors: [Theauxm]
repos: [cli, samples]
areas: [platform, packaging]
status: accepted
---

# Kubernetes is a deploy target beside compose and AWS, and both AWS runtimes share one foundation

`trax deploy` gains a Kubernetes target rendered from the same `trax.deploy.json` and the same host report as
compose and AWS ([0060](./0060-a-trax-app-is-deployed-from-what-its-host-reports.md)). It is the one interface
GCP, AWS and Azure all sell, so a single target covers moving an app between them; compose stays for one host,
and ECS stays as the cheapest way onto AWS. Decided 2026-11-16 and 2026-11-17.

## Status

**Accepted.**

## What was decided

- **Each environment names its own target**: `"target": "compose"`, `"aws"` (ECS Fargate) or `"kubernetes"`. One
  file can hold staging on kind and production on ECS.
- **The Kubernetes target never creates a cluster**, on any cloud. It applies plain manifests to the cluster the
  current kubeconfig context names, with the Kubernetes .NET client: the migrate Job first, then the Deployments.
  The hub's Deployment uses `Recreate`, so two hubs never run at once; workers roll.
- **Both ways onto AWS stay, over one foundation.** The AWS foundation (network, closed security groups, RDS,
  Secrets Manager, Cognito) is its own CloudFormation stack per environment. ECS is a runtime stack on top of it;
  Kubernetes on EKS is the Kubernetes target pointed at a cluster in the same network, with either side bringing
  the VPC. On EKS the host reads its secrets from Secrets Manager through EKS Pod Identity, with
  `AddTraxAwsSecretsAsync` from the opt-in `Trax.Effect.Configuration.Aws` package (the database password included,
  set on the connection string), and the database admits the app's pods through Security Groups for Pods; admitting
  the cluster's nodes, and so every pod on them, is a choice the environment must name. trax applies only to the
  current kubeconfig context, after EKS says its server is the named cluster's.
- **Postgres off AWS** is either an existing database named by secret, or a StatefulSet that the production profile
  refuses. Trax provisions no database on any other cloud.
- **Every target renders one hardening set**: capabilities dropped and named ones added back, no privilege
  escalation, pids and memory limits, a user namespace per service where the runtime has one.
- **Exposure and egress are manifest choices** that every target renders. Exposure is tailnet only at first.

## Rejected

- **One AWS stack with the runtime switched by the manifest.** The foundation could not be shared between runtimes
  or outlive one of them.
- **An AWS `runtime` field** choosing ECS or EKS. It makes Kubernetes an AWS option rather than the portable target.
- **Trax creating EKS clusters.** Trax would own control-plane upgrades and the control-plane bill.
- **EKS replacing ECS.** ECS Fargate is the cheaper floor for one small app, and a cluster is a second system to run.
- **Per-cloud database provisioning** outside AWS. Each cloud's database is a target of its own size.
- **A Helm chart or a Kustomize overlay.** `podman kube play` reads neither, and customization goes through
  `trax.deploy.json`. Revisit when someone asks.
- **An operator with CRDs** (a `TraxApp` resource). More to version and to secure; revisit if plain manifests prove
  too static.
- **Shelling out to `kubectl`**, which ties behaviour to the installed version, and **writing manifests only**,
  which leaves the migrate-before-rollout order to the user.
- **Kubernetes as the only target.** One host for one person should not need a cluster.
- **AWS Emulator as the test bed.** Its CloudFormation reports resources complete that it never provisions, several
  ELBv2 and ECR operations answer 501, it says not to use it for security testing, and its telemetry is on by default.

## Consequences

- Every field `trax.deploy.json` gains (exposure, egress, memory) needs an answer on every target, or a refusal.
- A single-stack AWS deployment from before the split is redeployed, not migrated in place.
- A deploy on Kubernetes has a few seconds with no hub, the price of never running two.

## Exemplars

**Enforced elsewhere:** `DeployKubernetesTests` and `KubernetesDeployerTests` in `Trax.Cli/tests/Trax.Cli.Tests/`
hold the Kubernetes target to it: each environment names its target, the hub's Deployment is `Recreate` at one
copy, the in-cluster database is refused under production, every container gets the hardening compose gives the
same service, and the migrate Job runs before any Deployment is applied. `EksDeployTests` holds the EKS side: no
secret reaches a manifest, the pods' role reads only the environment's secrets, the database admits the nodes only
when the environment says so, and a host that does not read its secrets from AWS, or a context that is not the named
cluster, is refused before anything changes. `TraxAwsSecretsTests` in `Trax.Effect` holds the reader.

## Changelog

- **2026-11-23**: On EKS: how the database password reaches the host, pod security groups by default with node access
  by name, and the context checked against the named cluster (user).
- **2026-11-17**: Recorded.
- **2026-11-17**: Named the tests that hold the Kubernetes target to it.
