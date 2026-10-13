---
title: Prepare a template for deployment
description: "Replace the Development-only demo key and dashboard posture of a Trax template project before running it anywhere but your machine."
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
| The Trax Dashboard at `/trax` | `trax-scheduler`, `trax-hub` | Not mapped; `/trax` is a 404. |
| The demo API key (`X-Api-Key: demo-key-do-not-use-in-production`) | `trax-api`, `trax-hub` | Not registered. Every template train and the `Note` query model carry `[TraxAuthorize]`, so every operation is refused. |

## Serve the dashboard outside Development

The dashboard can queue, run and cancel trains and change scheduler settings, and
`AllowAnonymousDashboard()` puts no authorization in front of it.

1. Register an authentication scheme that identifies your operators.
2. Replace `AllowAnonymousDashboard()` with `RequirePolicy("<policy>")` or
   `RequireRoles("<role>")` (see [UseTraxDashboard](/docs/sdk-reference/dashboard-api/use-trax-dashboard)).
3. Remove the `IsDevelopment()` checks around `AddTraxDashboard` and `UseTraxDashboard`.

A policy or roles with no authentication scheme to challenge with refuses to start, naming
`AddAuthentication`, because every request the dashboard refused would otherwise be a 500 rather
than a 401 or 403.

## Replace the demo key

1. Register real credentials, such as `AddHashed` keys loaded from a secret store, or
   [`AddTraxJwtAuth`](/docs/sdk-reference/api-auth/add-trax-jwt-auth).
2. Then remove the `IsDevelopment()` check around `AddTraxApiKeyAuth`, with the demo key gone from
   it. Moving the demo key out of the check does not work: a host refuses to start outside
   Development with a key containing `do-not-use-in-production` registered.
3. Keep `[TraxAuthorize]` on the trains you add. Mark one `[TraxAllowAnonymous]` only when anyone
   who can reach the host may call it.

[Secure a GraphQL host](/docs/api-security) covers the rest of a production posture.
