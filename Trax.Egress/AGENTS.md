# Trax.Egress

The egress image, `ghcr.io/traxsharp/trax-egress`: what `trax deploy` puts beside a service that names `"egress"`
in `trax.deploy.json`, and what a stack not deployed by Trax can run the same way. It holds a pod's outbound traffic
to a list of names. Go, standard library only, in a Dockerfile; no .NET build, no NuGet package. Released at the
Trax version by the Release workflow, which packs its digest into Trax.Cli
([`Trax.Docs/adr/0064`](../Trax.Docs/adr/0064-egress-is-one-image-released-with-trax.md)).

What it does and how to run it by hand: [`Trax.Docs/reference/egress-image.md`](../Trax.Docs/reference/egress-image.md).

| Path | What |
| --- | --- |
| `cmd/trax-egress/` | The five commands: `rules`, `serve`, `run`, `ready`, `check`. The uid and ports here are the ones Trax.Cli renders (`EgressContractTests` there reads this file). |
| `internal/allow/` | The list: its grammar, and the one reader every command uses. |
| `internal/rules/` | The nftables table, replaced whole in one transaction. |
| `internal/resolver/` | The DNS server: allowed names forwarded as a fresh query, every other name NXDOMAIN. |
| `internal/proxy/` | Where every app TCP connection lands: TCP entries by resolved address, everything else by SNI. |
| `internal/check/` | The probes a stack runs from inside the pod. |
| `internal/peers/` | The processes in the pod that run as the egress uid: `serve` refuses to start beside one and stops when one appears. |
| `seccomp/` | The app containers' seccomp profile, its generator and the default it is generated from. |
| `test/e2e.sh` | The image in real network namespaces under rootless podman. CI runs it. |

## Running the tests

The Go tests and the e2e run in podman, never on the host:

```bash
podman run --rm --network none -v "$PWD:/src:ro" -w /src -e GOCACHE=/tmp/gc -e GOTOOLCHAIN=local \
  docker.io/library/golang:1.25-alpine go test ./...
podman build -t localhost/trax-egress:dev .
test/e2e.sh localhost/trax-egress:dev
```

`E2E_DNS=1.1.1.1` runs the e2e on a host whose podman DNS (aardvark) does not answer on new networks.

## Rules that are easy to break

- **The app must never share the egress uid (61000).** The rules let that uid out; `serve` refuses any other, and
  refuses to run while another process it can see (one outside its own mount namespace) runs as it, and
  `check` refuses that one.
- **The rules fail closed.** The table's output policy is drop, and the sidecar only adds the way out. A change that
  makes anything depend on the sidecar running to stay blocked is wrong.
- **The proxy connects to what the name resolves to, never to the address the app dialled**, and a TLS entry only
  to public addresses. Both are what makes an allowed name on a shared CDN harmless.
- **A new probe in `check` must have a positive control** or be paired with one, so it cannot pass by not working.
