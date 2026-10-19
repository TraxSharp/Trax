---
authors: [Theauxm]
repos: [api, dashboard, scheduler, samples, docs]
areas: [platform]
status: accepted
---

# The React console is the operator dashboard, and the Blazor one is removed

Trax shipped two operator dashboards: `Trax.Dashboard`, Blazor Server in the host's process, and the console in
`Trax.Api.Dashboard`, a React app the host serves over its GraphQL API. [0022](./0022-the-dashboard-and-the-api-share-one-operation-per-action.md)
made every action land on both. From 2026-10-18 the console is the one operator dashboard. `Trax.Dashboard` takes
fixes only, no new page or action, and is removed in the release that ships the console's redesign. A new operator
action is a shared service method, the `operations` field that calls it, and the console control that calls the
field.

## Status

**Accepted.** Amends 0022: its shared-method rule stands, and the control it requires is the console's.
The removal is done: `Trax.Dashboard` is deleted from the repository, and every sample and template
serves the console instead.

## Considered options

**Keep both.** Every page and action built and tested twice, in two UI stacks, for one operator. The console was
already a superset of the Blazor dashboard's actions; the one behaviour only Blazor had (a live machine instance
page) is a subscription the API already serves. Users found both hard to follow, so a redesign was due, and doing it
twice doubles it for nothing.

**Mark it obsolete for a release, then remove it.** The usual path for a package with outside consumers. Trax has
one consumer, and the obsolete warning fails every sample's `-warnaserror` build, so the samples move to the console
first either way. An obsolete release would cost a release and teach nobody anything.

**Keep the Blazor dashboard and drop the console.** The console is the frontend that proves the API is enough to
build on (0022), runs under a strict CSP with persisted documents (api/0042), and needs no Blazor circuit in the
host. The Blazor dashboard reads some lists straight from EF, which no other frontend can do.

## Consequences

Until the removal, a fix to the Blazor dashboard is still allowed and still goes through the shared method.
The Blazor dashboard's own ADRs (`Trax.Dashboard/docs/adr/`) leave with its folder; its authorization-posture rule
lives on as api/0039.

## Exemplars

**Enforced elsewhere:** `OperationsIsAdminOnlyTests` in Trax.Api fails when an `operations` field has no console
document that selects it (api/0041), so a new action cannot reach the API without its console control.

Not covered: nothing stops a new page or action being added to `Trax.Dashboard` before it is removed. The freeze is
held by review, because a guard against a folder that is about to be deleted would be deleted with it.

## Changelog

- **2026-10-19**: The removal happened. The `Trax.Dashboard` folder, its CI row and its pins are
  gone, the samples and templates serve the console with `AllowAnonymousShell()` in Development, and
  the package is deprecated on nuget.org at release, by hand.
- **2026-10-18**: Recorded.
