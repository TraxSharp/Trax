---
title: The egress image
description: "The trax-egress image: what it allows and refuses, its allowlist, its five commands, its seccomp profile, and how to run it beside any container stack."
type: reference
area: tooling
nav_order: 7
source:
  - Trax.Egress
---

# The egress image

`ghcr.io/traxsharp/trax-egress` limits what a container may reach to a list of domains. `trax deploy` puts it beside
every service that names `"egress"` in `trax.deploy.json` ([the CLI reference](/docs/reference/cli#egress)), and any
other container stack can run it the same way. It is released with Trax, at the Trax version, and a trax of a
version names the image of the same version by digest. It is built for `linux/amd64` and `linux/arm64`, and the
digest is that of the index holding both, so the same digest pulls the right one on either architecture.

## What it does

The image runs twice in a pod (or once, under compose; see [Commands](#commands)):

1. **`rules`**, as root with `NET_ADMIN`, writes one nftables table into the pod's network namespace and exits. The
   table's output policy is drop. Every process but the egress's own (uid 61000) has its DNS sent to the egress
   resolver and its IPv4 TCP sent to the egress proxy, whatever address it dialled. Everything else it sends is
   refused: UDP other than DNS, IPv6, and loopback addresses other than 127.0.0.1.
2. **`serve`**, as uid 61000 with no capability, runs the resolver and the proxy on loopback.

The app keeps every capability dropped, so it cannot change the rules. If `serve` is not running, nothing leaves the
pod at all.

| The app tries | What happens |
|---|---|
| A name on the list, over TLS on its port | The proxy reads the name the client asks for (SNI), resolves it, and connects to a public address the name resolves to. |
| The same name on another port | Reset. |
| An unlisted name | The resolver answers NXDOMAIN. The name never leaves the pod. |
| A listed name's AAAA, TXT or other record | An empty answer. Only A records are looked up. |
| DNS to another resolver (`8.8.8.8:53`) | Sent to the egress resolver anyway. |
| An address, with the SNI of a listed name | Connected to where the name resolves, not to the address dialled. |
| An address, with any other SNI or none | Reset. |
| Instance metadata (`169.254.169.254`), EKS Pod Identity (`169.254.170.23`), the LAN, the tailnet | Reset, unless a `tcp:` entry names it. |
| A listed name that resolves to a private or link-local address | Reset. A TLS entry reaches public addresses only. |
| One of the deployment's own services (`tcp:database:5432`) | Passed through when the address dialled is one the name resolves to now. |
| `nft flush ruleset` | Refused: the app has no `NET_ADMIN`. |

What it does not do: it does not read inside TLS, so a client that sends one name in its SNI and another in its
`Host` header reaches the first name's server with the second name's request (domain fronting). The server decides
what to do with it. Encrypted Client Hello hides the inner name; the proxy decides on the outer one.

## The list

Every command reads the list from the same places, so the rules, the sidecar and the check agree:

| Variable | Holds |
|---|---|
| `TRAX_EGRESS_ALLOW` | Entries separated by spaces, commas or new lines. |
| `TRAX_EGRESS_ALLOW_FILE` | A file of entries, one a line, `#` starting a comment. |
| `TRAX_EGRESS_POSTGRES` | An Npgsql connection string. Its hosts (`Host`, with `Port` or a `:port` each) become `tcp:` entries; nothing else in it is kept. |

An entry is one of:

| Entry | Means |
|---|---|
| `api.anthropic.com` | TLS to that name on 443. |
| `registry.example.com:8443` | TLS to that name on 8443. |
| `tcp:database:5432` | Plain TCP to whatever `database` resolves to now, private addresses included. For the deployment's own services. |
| `tcp:169.254.170.23:80` | Plain TCP to that one address and port. |

A name is lower case: letters, digits and dashes in labels of at most 63, at least two labels for a TLS entry. A
wildcard, a scheme, a path, an address in place of a TLS name, and a port outside 1 to 65535 are refused, and `rules`
exits non-zero, so a pod with a bad list never starts. An empty list allows nothing but the `tcp:` entries.

## Commands

| Command | Runs as | Does |
|---|---|---|
| `rules` | root, `NET_ADMIN` only | Applies the table and exits. An init container. |
| `serve` | uid 61000, no capabilities | Runs the resolver (`127.0.0.1:15053`) and the proxy (`127.0.0.1:15001`). Refuses to start as any other uid. The image's default. |
| `run` | root, `NET_ADMIN`, `SETUID`, `SETGID` | `rules`, then gives up root and every capability (and checks they are gone), then `serve`. For compose, which has no init container. |
| `ready` | any | Exits 0 when the resolver and the proxy are listening. A startup probe or a healthcheck. |
| `check` | the app's uid | Probes from inside the pod. See [Proving it](#proving-it). |

The app must not run as uid 61000: the rules let that uid out.

## Running it beside a stack

Under compose, as `trax deploy` writes it, the app joins the sidecar's network namespace:

```yaml
services:
  app-egress:
    image: ghcr.io/traxsharp/trax-egress@sha256:<digest>
    user: "0:0"
    command: ["run"]
    environment:
      TRAX_EGRESS_ALLOW: "api.anthropic.com tcp:database:5432"
    cap_drop: [ALL]
    cap_add: [NET_ADMIN, SETUID, SETGID]
    security_opt: ["no-new-privileges:true"]
    read_only: true
    pids_limit: 256
    healthcheck:
      test: ["CMD", "/usr/local/bin/trax-egress", "ready"]
      interval: 2s
      retries: 30
  app:
    image: my-app
    network_mode: "service:app-egress"
    depends_on:
      app-egress:
        condition: service_healthy
```

Ports the app serves are published on the sidecar, since it owns the namespace. On Kubernetes, `rules` is an init
container and `serve` a native sidecar (an init container with `restartPolicy: Always`) with `ready` as its startup
probe; `trax deploy --target kubernetes` renders both and the admission policy that allows `NET_ADMIN` on the first
alone. `podman kube play` does not run native sidecars (it waits for every init container to exit), so there the
sidecar is an ordinary container running `run`, and the `rules` init container runs on every start of the pod. The
sidecar putting the rules back matters: `podman pod restart` gives the pod a new network namespace without running
init containers again.

## Proving it

`check` runs as the app would and probes what the list allows (each must open) and what it must not (each must be
blocked): an unlisted name, the same name asked of `1.1.1.1` directly, a TLS connection to `1.1.1.1:443` asking for
it, instance metadata, the EKS Pod Identity agent, each private range, the tailnet's resolver, a public IPv6 address,
and `nft list ruleset` and `nft flush ruleset`. Add more with `TRAX_EGRESS_CHECK_DENY` (`host:port`, separated by
spaces or commas), such as the host's WAN address or a peer. Its first probe is a control: a connection to
`192.0.2.1:9`, which nothing answers, must be intercepted by the proxy. Without it a check run outside the egress
would report everything blocked.

```bash
podman run --rm --pod my-pod --user 1654:1654 --cap-drop ALL \
  -e TRAX_EGRESS_ALLOW="api.anthropic.com tcp:database:5432" \
  -e TRAX_EGRESS_CHECK_DENY="203.0.113.7:443" \
  ghcr.io/traxsharp/trax-egress@sha256:<digest> check
```

It prints one line a probe and exits 1 when any probe did not do what it must. On Kubernetes, run it as an ephemeral
container in the pod, with the same environment and the app's uid. Use `kubectl debug`'s `restricted` profile: the
`general` one adds `SYS_PTRACE`, which the namespace's admission policy refuses. A command after `--` replaces the
image's entrypoint, so name the binary.

```bash
printf '%s' '{"env":[{"name":"TRAX_EGRESS_ALLOW","value":"api.anthropic.com tcp:database:5432"}],
  "securityContext":{"runAsUser":1654,"runAsGroup":1654}}' > check.json
kubectl -n orders-staging debug pod/<hub pod> --image=ghcr.io/traxsharp/trax-egress@sha256:<digest> \
  --container=egress-check --profile=restricted --custom=check.json --attach=false -- /usr/local/bin/trax-egress check
kubectl -n orders-staging logs <hub pod> -c egress-check
```

## The seccomp profile

The image carries `/usr/share/trax-egress/seccomp/apps-nouserns.json`, a seccomp profile for app containers: the
containers/common default, without new user namespaces, the mount API or the kernel keyring. Its generator and the
default it is generated from are in `Trax.Egress/seccomp/`, and CI fails when the committed profile is not what the
generator writes. To use it under compose or podman:

```bash
podman run --rm --entrypoint cat ghcr.io/traxsharp/trax-egress@sha256:<digest> \
  /usr/share/trax-egress/seccomp/apps-nouserns.json > apps-nouserns.json
```

and name it with `security_opt: ["seccomp=apps-nouserns.json"]`. trax deploy does not apply it: a cluster reads a
`Localhost` profile from each node's disk, which trax does not manage.
