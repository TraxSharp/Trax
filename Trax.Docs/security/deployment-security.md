---
title: Deployment security
description: "What each layer of a trax deploy keeps out, from container hardening to network policies, tailnet exposure and egress, and what has been proved."
type: explanation
area: security
nav_order: 20
---

# Deployment security

A Trax app deployed with `trax deploy` sits inside several layers, each written by the deploy rather than by you.
This page says what each one keeps out, what was proved by running it on real hosts, and what has not been. Two
layers have pages of their own and are linked, not repeated: [Tailnet sign-in](/docs/security/tailnet-sign-in) and
the [sandbox threat model](/docs/security/sandbox-threat-model). How the deploy itself runs is in
[How a deploy works](/docs/reference/how-a-deploy-works).

## The layers

| Layer | Keeps out | Where |
|---|---|---|
| Container hardening | A compromised process gaining privileges: every capability dropped (Postgres gets back the few its entrypoint needs, and so does RabbitMQ under compose; on a cluster and under the podman runtime RabbitMQ runs as its own user with none), no privilege escalation, the runtime's default seccomp profile, a non-root user for every Trax container, memory and CPU limits when the service names them. | compose, the podman runtime and a cluster. An ECS task drops every capability and gets the read-only root and its Fargate size; Fargate has no seccomp profile or `no-new-privileges` setting to give it. |
| A read-only root | A compromised process changing its own binaries or leaving a file that survives a restart. A Trax container writes only to a scratch `/tmp`. | Every target, for every Trax container and Job. Postgres and RabbitMQ keep a writable root. |
| A user namespace | An escape landing as root on the machine. Root in the container maps to an unprivileged uid outside it; under podman, `nomap` also leaves your own uid out, so an escape does not land as you. | `hostUsers: false` on a cluster, `nomap` under podman. Not under docker compose, which refuses `nomap`, and not on ECS. |
| No token, no service links | A compromised pod calling the Kubernetes API as a ServiceAccount, or reading other services' addresses from its environment. | Every pod on a cluster. On EKS a pod gets a role through Pod Identity that reads only its environment's secrets. |
| Network policies | Other pods in the cluster reaching the hub, Postgres or RabbitMQ. A deny-all policy selects every pod in the namespace, and one more admits the app's pods to each other. They limit what comes in, not what goes out. | Every cluster deploy, before anything they protect. Only on a network plugin that enforces NetworkPolicy; on one that does not, they are accepted and do nothing. |
| No published address | Anything outside the cluster or the host reaching the app. Without an exposure, nothing does: `kubectl port-forward` on a cluster, `127.0.0.1` on one host. | Every kubernetes and compose deploy. ECS and its load balancer are public by design. |
| Tailnet exposure and the proxy secret | Anyone but your tailnet's people and devices reaching the hub, and anyone who reaches it some other way signing in as someone. See [Tailnet sign-in](/docs/security/tailnet-sign-in). | `"exposure": "tailnet"` on a kubernetes environment. |
| Secrets kept out of manifests | A secret value in a file that is committed, logged or applied for anyone to read. Values come from a Secret you create (by `secretKeyRef`), from Secrets Manager on EKS and ECS, or from a 0600 file under `~/.config/trax/` under podman. The passwords of the Postgres and RabbitMQ trax runs reach the hosts as values of their own, never inside a connection string it writes. | Every target. |
| Egress | A compromised service sending data anywhere, or reaching instance metadata, the LAN or the tailnet: it reaches the domains it names and the deployment's own database and broker. The app keeps every capability dropped, so it cannot change the rules, and on a cluster an admission policy refuses `NET_ADMIN` to every container but the rules' own init container. See [the egress image](/docs/reference/egress-image) for what it does and does not stop. | A service that names `"egress"`, on compose and a cluster. Refused on ECS. On the podman runtime it is rendered but does not work yet: a held service cannot reach Postgres there. |
| Ownership checks | One app's deploy moving another app's services onto its images, or applying over objects it did not create. | A cluster deploy refuses a namespace, an object or an egress policy labelled for another app, or one it would apply over that carries no label. |
| A virtual machine around k3s | A container that escapes being root on the machine your keys live on. k3s runs as root; inside a VM, root holds only the app. | Your choice, which [Deploy to one machine with no downtime](/docs/reference/deploy-to-one-machine-with-no-downtime) makes. |
| A Kata sandbox | Untrusted work reaching anything at all. See the [sandbox threat model](/docs/security/sandbox-threat-model). | A `[TraxSandbox]` train, through the launcher on a cluster with a Kata RuntimeClass. |

The drain and the rollback are not security layers. They keep a release from cutting off work or taking the API
down, and they do it without opening anything: the drain runs inside the cluster on the app's own image, and needs
no endpoint into the app.

## What has been proved

`.github/ci/deploy/cluster.sh` and `podman.sh` run `trax deploy` for real, against the DistributedWorkers sample (a
hub, two workers, Postgres and RabbitMQ, from `Trax.Samples/samples/DistributedWorkers/trax.deploy.json`) built from
the same commit, and check what it did. They are the `deploy-cluster` CI job, and `cluster.sh` also runs against an
existing cluster. Both were run on k3s v1.36.6 in a Debian 13 virtual machine on 2026-11-24:

- **A first deploy.** The Secret was checked, Postgres and RabbitMQ came up in the cluster under their limits, the
  migrate Job ran, the hub and both workers rolled out, and `/trax/health` and GraphQL answered.
- **Hardening.** Every hub and worker pod ran with `hostUsers: false`, no service account token, a read-only root and
  its service's memory and CPU.
- **Network policy.** A pod labelled as the app reached Postgres and RabbitMQ; the same probe from a pod that was not
  was refused by both. The first is the control that makes the second mean the policy.
- **The split.** A worker finished a run of the hub's schedule, and the hub and a worker were both connected to
  RabbitMQ.
- **The drain.** With a run InProgress, the drain Job reported it busy and neither the hub's nor the workers' image
  changed until the run ended. Then the hub rolled with two hub pods running at once, and a probe inside the
  namespace that asked the Service for `/trax/health` five times a second for the whole deploy saw 332 answers and no
  failure. The run was left as it had been ended: the new hub's recovery did not touch it. The drain row was gone
  afterwards.
- **Rollback.** A release whose hub cannot serve failed at the Deployment's progress deadline, naming the hub, and
  the hub and workers that had been serving served again on their old images.
- **Egress.** With the egress image built from the same commit and named by digest, the workers on the sample's empty
  list and the hub on `api.github.com`, everything rolled out healthy, a worker finished a new run and was connected to
  RabbitMQ, so both reached what they needed through the egress. Each pod had the rules init container and the sidecar,
  and still `hostUsers: false`. The egress check, run inside each pod as the app's uid, found what that pod's list
  allows open (the hub: `api.github.com`, the database and the broker; a worker: the database and the broker) and
  other names, `api.github.com` from a worker, DNS to an outside resolver, metadata, the private ranges and
  `nft flush ruleset` refused. The admission policy refused `NET_ADMIN` on an app container, a rules container running
  another image, and a `kubectl debug` container adding `SYS_PTRACE`.

Under podman, with the units running under `systemd --user`: the first deploy started Postgres and RabbitMQ, ran the
migration and served a healthy hub on `127.0.0.1` and a worker, both with a read-only root; the worker ran the hub's
schedule and both were connected to RabbitMQ; a release that changed only the images drained first, held while a run
was busy, then restarted only the hub and the worker and left Postgres and RabbitMQ running.

The sandbox escape suite ran through the launcher on k3s with a Kata RuntimeClass, and every case passed (15 of 15);
the [threat model](/docs/security/sandbox-threat-model#hostile-work-in-a-sandbox) lists each one.

## What has not been proved

- **A real tailnet login.** The tailnet exposure's manifests and the sign-in's refusals are tested, but no deploy
  has been run with the Tailscale operator and a person signing in through it.
- **Managed clusters.** Nothing has been run on EKS, GKE or AKS, nor ECS. What those targets render is held by unit
  tests, not by a deploy.
- **NetworkPolicy and `hostUsers: false` beyond k3s.** Both were proved on k3s's own network policy controller and a
  VM kernel of 6.3 or later. A network plugin that does not enforce NetworkPolicy accepts the policies and ignores
  them, and nothing says so.
- **A kernel escape against Kata.** The suite shows a sandbox runs on a guest kernel, not that the guest cannot be
  broken out of.
- **ECS egress.** It is refused, not built, so there is nothing to prove; a service that needs it goes to EKS.
- **The seccomp profile in the egress image** for app containers is not applied by `trax deploy`: a cluster reads a
  `Localhost` profile from each node's disk, which trax does not manage.
- **The compose target under docker** runs without a user namespace, since docker refuses `nomap`.

## SDK Reference

> [AddTraxTailscaleAuth](/docs/sdk-reference/api-auth/add-trax-tailscale-auth) | [AddTraxAwsSecretsAsync](/docs/sdk-reference/configuration/add-trax-aws-secrets) | [TraxSandbox](/docs/sdk-reference/attributes/trax-sandbox) | [TraxDrain](/docs/sdk-reference/configuration/trax-drain)
