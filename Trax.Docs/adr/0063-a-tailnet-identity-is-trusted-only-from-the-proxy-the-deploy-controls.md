---
authors: [Theauxm]
repos: [api, cli, effect]
areas: [platform]
status: accepted
---

# A tailnet identity is trusted only from the proxy the deploy controls

A Trax host can sign people in by the login Tailscale serve puts in `Tailscale-User-Login`
(`AddTraxTailscaleAuth`, Trax.Api.Auth.Tailscale), and `trax deploy` can put a kubernetes environment's hub on a
tailnet (`"exposure": "tailnet"`). The two are one decision, because the header is only as trustworthy as the path
it came by. Decided with the user on 2026-11-16 and 2026-11-17 (MS-21); this records the shape it was built in on
2026-11-24. It overturns MS-14's line that the private side belongs outside Trax, for exposure, as
[0062](./0062-kubernetes-is-a-deploy-target-beside-compose-and-aws.md) foresaw.

## Status

**Accepted.**

## What was decided

- **Trust comes from the transport, never from a source address.** The scheme does not read the request's address,
  so forwarded headers cannot change its decision. A request carries a tailnet identity only when it carries the
  proxy's secret in `Trax-Proxy-Secret`, compared in constant time over SHA-256 digests; anything else is anonymous.
- **Tailscale serve cannot add a header, so trax renders an edge that does.** A Caddy proxy between serve and the hub
  sets the secret on every request, replacing a caller's copy. On a cluster it is its own Deployment behind the
  Tailscale operator's Ingress; under podman it shares a pod with Tailscale, beside the hub's, and listens only on a
  Unix socket that serve dials, and the hub publishes no host port. Tailscale runs in userspace networking there,
  which hands a tailnet connection serve does not take to `127.0.0.1` on the same port, so nothing in its network
  namespace listens on TCP and the hub is not in it.
- **On a cluster, both a network policy and the secret.** Every namespace trax deploys gets a default-deny ingress
  policy; the app's pods reach each other, the edge accepts only the operator's proxy pod for its Ingress, and the hub
  accepts the edge. A plugin that ignores NetworkPolicy leaves the secret.
- **The secret is minted by `trax deploy` on every deploy** and held in a Secret (`<app>-<env>-tailnet`) or, under
  podman, a 0600 file only the hub's unit and the tailnet unit play. Only the hub and the edge read it.
- **Exposure is a deployment fact the host is told.** `trax deploy` sets `Trax__Deploy__Exposure=tailnet` only on the
  exposed hub. The scheme refuses to start without it or without a secret of 32 characters, in every environment. The
  host reports the scheme in its deploy manifest, and `trax deploy` refuses it on compose, on AWS, on an unexposed
  kubernetes environment and on any service but the exposed hub. A deploy file may not set `Trax__Deploy__*` itself.
- **Roles come from logins.** Tailscale sends no identity for a tagged device, so tags cannot carry roles. A login
  the host does not name is refused unless it admits every login.
- **Exposure is `tailnet` only.** `public` is refused until it is built.

## Rejected

- **Trusting the proxy's address or a network range.** Pod addresses are shared and move; `KnownNetworks` on AWS is
  the whole VPC, so `X-Forwarded-For` lets any pod in it name the proxy; on one host, loopback is every process and a
  browser through DNS rebinding.
- **The operator's Service mode.** It forwards TCP and adds no identity headers.
- **A network policy alone, or the secret alone.** The policy holds only where the plugin enforces it; the secret
  alone leaves the in-cluster database and broker open to every pod, as they were.
- **A tun device for the podman Tailscale, or keeping it in the hub's pod.** Tun mode needs `NET_ADMIN` and
  `/dev/net/tun`, and delivers tailnet traffic to whatever listens on the pod's addresses, the hub included. In the
  hub's pod, userspace mode reaches the hub's port and the edge's through loopback.
- **Trax's own sidecar on a cluster instead of the operator.** The user chose the operator, which holds its own
  credentials, so trax handles no Tailscale credential on a cluster.

## Consequences

- A cluster needs the Tailscale operator in its standalone mode; its HA `ProxyGroup` labels pods differently and the
  edge's policy would admit nothing.
- A deploy rotates the secret and restarts the hub and the edge; requests in the seconds between are anonymous.
- A subscription socket sees the tailnet caller only when the host makes the scheme its default.
- Every kubernetes environment now runs under default-deny ingress, exposed or not.

## Exemplars

**Enforced elsewhere:** `TailscaleAuthE2ETests` and `TailscaleTrustStartupTests` in
`Trax.Api/tests/Trax.Api.Tests/` hold the scheme to it: no secret or a wrong one is anonymous whatever the address
or forwarded-for says, a duplicate or comma-joined login is refused, a tagged device is anonymous, and the host does
not start unexposed or without a secret. `TailscalePartsTests` pins the constant-time comparison. `DeployTailnetTests`
in `Trax.Cli/tests/Trax.Cli.Tests/` holds the deploy to it: the policies' shape, no LoadBalancer, NodePort or other
Ingress, the edge, the secret given only to the hub and minted on every deploy, no published port under podman, and
every refusal of the field and of the scheme, and under podman, a pod of their own for Tailscale and the edge with no
TCP listener in it.

## Changelog

- **2026-11-24**: Recorded.
- **2026-11-25**: Under podman, Tailscale and the edge moved out of the hub's pod into one of their own, the edge onto
  a Unix socket: userspace tailscaled forwards a tailnet connection serve does not take to loopback.
