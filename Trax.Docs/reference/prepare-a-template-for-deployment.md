---
title: Prepare a template for deployment
description: "Replace the Development-only demo keys and console posture of a Trax template project before running it anywhere but your machine."
type: how-to
area: tooling
nav_order: 5
source:
  - Trax.Samples/templates/content
---

# Prepare a template for deployment

The [project templates](/docs/reference/templates) start in Development: `dotnet run` picks up
`ASPNETCORE_ENVIRONMENT=Development` from `Properties/launchSettings.json`. A published build
started with `dotnet MyCompany.Hub.dll`, or in a container, runs in Production unless the
environment says otherwise, and two things exist only in Development:

| What | Where | Outside Development |
|------|-------|---------------------|
| The operations console at `/trax/dashboard` | `trax-scheduler`, `trax-hub` | Not mapped; `/trax/dashboard` is a 404. |
| The demo API key (`X-Api-Key: demo-key-do-not-use-in-production`) | `trax-api`, `trax-hub` | Not registered. Every template train and the `Note` query model carry `[TraxAuthorize]`, so every operation is refused. |
| The operator key (`X-Api-Key: operator-key-do-not-use-in-production`) | `trax-scheduler`, `trax-hub` | Not registered. The `operations` namespace is gated to `Operator`, so the console's every read and write is refused. |

## Serve the console outside Development

The console can queue, run and cancel trains and change scheduler settings, and
`AllowAnonymousShell()` puts no authorization in front of its page. What it reads and changes is
still gated by `GateOperations`.

1. Register a sign-in the browser carries on a page load, a cookie or OpenID Connect, for your
   operators. A page load sends no `X-Api-Key` or `Authorization` header, so an API key or a
   bearer token cannot gate the page.
2. Replace `AllowAnonymousShell()` with `RequirePolicy("<policy>")` or `RequireRoles("<role>")`
   (see [UseTraxApiDashboard](/docs/sdk-reference/api-dashboard/use-trax-api-dashboard)).
3. Remove the `IsDevelopment()` check around `UseTraxApiDashboard`.

A policy or roles with no authentication scheme to challenge with refuses to start, naming
`AddAuthentication`, because every request the console refused would otherwise be a 500 rather
than a 401 or 403.

## Replace the demo keys

1. Register real credentials, such as `AddHashed` keys loaded from a secret store, or
   [`AddTraxJwtAuth`](/docs/sdk-reference/api-auth/add-trax-jwt-auth).
2. Then remove the `IsDevelopment()` check around `AddTraxApiKeyAuth`, with the demo keys gone from
   it. Moving the demo key out of the check does not work: a host refuses to start outside
   Development with a key containing `do-not-use-in-production` registered.
   That check sees only keys registered with `Add`. A host that resolves keys from its own store
   (`AddTraxApiKeyAuth<TResolver>()`) and seeds demo users into it in Development keeps them in
   Production: remove the seeding and the rows it wrote before deploying.
3. Keep `[TraxAuthorize]` on the trains you add. Mark one `[TraxAllowAnonymous]` only when anyone
   who can reach the host may call it.

## Behind a load balancer

A load balancer that ends TLS calls the host over plain HTTP and says who the caller was in
`X-Forwarded-For` and `X-Forwarded-Proto`. The templates believe those headers only from the
networks in `Trax:Proxy:KnownNetworks`, comma-separated CIDR ranges:

```bash
Trax__Proxy__KnownNetworks=10.0.0.0/16
```

Set it to the network the load balancer sends from, such as the VPC's range. With it unset the host
reads neither header, so behind a load balancer it sees every request as `http` from the load
balancer's address: a sign-in redirect is built with `http`, and anything keyed on the caller's
address sees one caller. The templates do not fall back to believing any sender, which is what
ASP.NET Core's forwarded headers middleware does when it is given no networks: then any client
could claim to have arrived over HTTPS, or from any address.

## Build a container image

Each template publishes as a container image with no Dockerfile:

```bash
dotnet publish -t:PublishContainer
dotnet publish -t:PublishContainer -p:ContainerRegistry=123456789012.dkr.ecr.us-west-2.amazonaws.com
```

The image is the ASP.NET runtime image with the published app, named after the project in lower case, running as
the runtime image's non-root user and listening on 8080, which the project declares with a `ContainerPort` item. The
first form loads it into the local Docker or Podman; the second pushes it to the registry instead.

[Secure a GraphQL host](/docs/security/secure-a-graphql-host) covers the rest of a production posture.

## SDK Reference

> [UseTraxApiDashboard](/docs/sdk-reference/api-dashboard/use-trax-api-dashboard) | [AddTraxJwtAuth](/docs/sdk-reference/api-auth/add-trax-jwt-auth)
