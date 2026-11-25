---
title: Tailnet sign-in
description: "Why tailnet sign-in trusts Tailscale identity headers only with a per-deploy secret from a proxy trax renders, never by the sender's address."
type: explanation
area: security
nav_order: 32
---

# Tailnet sign-in

[Tailscale serve](https://tailscale.com/kb/1312/serve) proxies a request from a device on your tailnet and tells the
app behind it who sent it, in `Tailscale-User-Login` and two more headers. An app that reads those headers signs
people in with no password and no identity provider of its own. It also signs in anyone who can reach the app without
going through serve and writes the header themselves. [`AddTraxTailscaleAuth`](/docs/sdk-reference/api-auth/add-trax-tailscale-auth)
is built around that second fact: the headers are trusted only where the deployment can prove they came through serve.
The rules are on the reference page; this page is about why they are shaped this way.

## What does not work: the sender's address

The obvious rule is "trust the headers when the request came from the proxy's address". It holds nowhere Trax
deploys:

- **On a cluster** the Tailscale operator's proxy is a pod with an address from the same range as every other pod,
  which changes when the pod is rescheduled. A rule broad enough to cover it covers its neighbours.
- **Behind forwarded headers** the address a host sees is whatever `X-Forwarded-For` says, once the sender is inside
  `KnownNetworks`. On AWS that is the whole VPC, so any pod in it can name the proxy's address and send a login with
  it.
- **On one host** "the proxy is on loopback" covers too much. A published `127.0.0.1` port is reachable by every
  process on the machine and by a browser through DNS rebinding, and rootless podman's port forwarding makes every
  local connection look like loopback.

So the scheme never reads the address. Forwarded headers can rewrite it freely without changing what the scheme
decides.

## What does: a secret only the proxy holds, and a path only the proxy can take

Serve cannot add a header of its own, so `trax deploy` puts a small Caddy proxy, the edge, between serve and the hub.
The edge sets `Trax-Proxy-Secret` on every request, replacing any copy the caller sent, to a value `trax deploy` mints
on every deploy and gives only to the hub and the edge. The hub reads the identity headers only from a request that
carries that value; a request without it is anonymous, whatever it claims.

On a cluster two things hold that together:

1. **The path.** Network policies refuse every connection in the namespace by default. The app's own pods reach each
   other, the edge reaches only the hub, and the edge accepts only the operator's proxy pod for its Ingress. Nothing
   else in the cluster can hand the edge a forged login and have it signed.
2. **The secret.** A pod that reaches the hub directly, which the policies allow for the app's own workers, has no
   secret, so its headers are ignored. A cluster whose network plugin does not enforce NetworkPolicy still has this
   half.

On one host there are no network policies, so the path is held by where things listen. Tailscale runs in userspace
networking there, and in that mode tailscaled hands any tailnet connection that serve does not take to `127.0.0.1` on
the same port. Anything listening on TCP in Tailscale's network namespace is therefore on the tailnet without serve's
headers being stripped. So Tailscale and the edge get a pod of their own, the edge listens only on a Unix socket in a
directory only that pod mounts, which serve dials, and the hub is in another pod, published on no host port. A
container on the app's network can reach the hub directly, and has no secret.

The secret is compared as two SHA-256 digests with a fixed-time comparison, so the time an answer takes says nothing
about how close a guess was. It changes on every deploy, so one that leaked from an old pod's environment stops
working at the next release.

## Exposure is the deployment's to say

Whether a host is reachable only through that proxy is a fact about the deployment, not about the code, and the host
cannot see it. So `trax deploy` tells it: it sets `Trax__Deploy__Exposure=tailnet` only on a hub it put behind the
edge, and the scheme refuses to start without it, in Development too. The rest of the chain refuses the other cases
before anything runs:

- the host reports the scheme in its deploy manifest, and `trax deploy` refuses it on compose, on AWS (a public load
  balancer), on a kubernetes environment that is not exposed on a tailnet, and on any service but the exposed hub;
- `trax.deploy.json` may not set anything under `Trax__Deploy__`, so an app cannot claim the exposure for itself.

A host run by hand behind `tailscale serve` can set both values itself. It then owns the guarantee the deploy would
have made: that nothing but its own edge reaches it, and that the edge sets the secret.

## What Tailscale does not tell you

Serve leaves the identity headers off for a request from a tagged device: a tag is a machine's identity, not a
person's. So roles come from logins only, and a tagged machine that needs to call the hub uses another scheme, such
as an API key. Serve also sends the headers for people a device was shared with, from outside your tailnet's
organization, which is why a login the host did not name is refused unless it chose `AnyLogin`.

## Rejected

- **Trusting a source address or range**, for the reasons above.
- **The operator's Service mode** in place of its Ingress. It forwards TCP and adds no headers at all.
- **Only a network policy**, without the secret. It holds only where the network plugin enforces it, and says
  nothing on one that silently does not.
- **Only the secret**, without the policies. The policies also keep the rest of the cluster away from the in-cluster
  database and broker, which carried no such protection before.
- **Roles from tags.** The headers do not exist for tagged devices.

## See also

- [AddTraxTailscaleAuth](/docs/sdk-reference/api-auth/add-trax-tailscale-auth)
- [`trax deploy`, exposed on a tailnet](/docs/reference/cli#exposed-on-a-tailnet)
- [Where Trax checks authorization](/docs/security/where-authorization-is-checked)
