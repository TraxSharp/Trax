---
title: "18.9 The console"
description: "Chapter 18 of the Trax book, section 9: The console."
type: tutorial
area: general
parent: "18. Deploy"
grand_parent: The Trax book
nav_order: 9
snippet_context: none
follows: book/deploy/the-storefront
---

# 18.9 The console

The console is served at `/trax/dashboard` with `AllowAnonymousShell()`, and the hub warns about it
every time it starts:

```text
warn: Trax.Api.Dashboard[0]
      AllowAnonymousShell() is set: the Trax API dashboard at /trax/dashboard applies no authorization of its own, so anyone who can reach it can load the app. What the app shows and changes is still authorized by the Trax GraphQL endpoint. Use RequirePolicy() or RequireRoles() unless something in front of it is the gate.
```

The shell is the page and its scripts. Everything it shows comes from the `operations` namespace,
which `GateOperations(roles: Roles.Staff)` refuses to anyone who is not staff, whoever loaded the
page. A page load cannot carry a bearer token, so a role on the shell itself needs a scheme the
browser sends, a cookie from OIDC sign-in. Until Shopfront has one, keep the shell anonymous and
put `/trax/dashboard` behind something that is the gate: an internal network, a VPN, or the
ingress's own sign-in.

## SDK Reference

> [Deployment topologies](/docs/reference/deployment-topologies) | [Move a domain data context to EF migrations](/docs/effect/move-a-domain-data-context-to-ef-migrations) | [UseRabbitMq](/docs/sdk-reference/configuration/use-rabbit-mq) | [AddTraxHealthCheck](/docs/sdk-reference/graphql-api/add-trax-health-check) | [Health and liveness](/docs/scheduler/health-and-liveness) | [Startup refusals](/docs/security/startup-refusals) | [UseTraxApiDashboard](/docs/sdk-reference/api-dashboard/use-trax-api-dashboard)

---

Next: [18.10 Before it takes real orders](/docs/book/deploy/before-it-takes-real-orders)
