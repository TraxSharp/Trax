---
authors: [Theauxm]
areas: [platform, ui]
status: accepted
---

# The dashboard mounts into the host's application

`Trax.Dashboard` is a Blazor Server component library, not a deployable. A host calls
`AddTraxDashboard()` and `UseTraxDashboard()` and the pages appear inside its own
application, at `/trax`. There is no separate process, no separate port, and no separate
deployment.

## Status

**Accepted.**

## Considered options

**A standalone dashboard application**, which is what most job schedulers ship. It is the
obvious shape and it was rejected because of what it drags in: its own host, its own
deployment, its own TLS and ingress, and its own notion of who the caller is. A separate app
looking at the same database has nothing to reuse and has to be told from scratch who may see
it.

Mounting into the host does not answer that question, it relocates it. The dashboard is
reachable wherever the host maps it and is gated by whatever the host applies to that path. A
host that registers a fallback authorization policy gates every endpoint and therefore gates
`/trax` too. A host that puts `[Authorize]` on its own controllers and pages, which is the
ordinary shape, gates `/trax` with nothing and serves it to anyone who can reach the port.
What mounting buys is an identity the dashboard could reuse, not a gate that is closed by
default.

**So closing it is the consumer's job, and it is not optional.** Either register a fallback
policy, so that every endpoint including this one requires an authenticated principal, or put
authorization middleware or an ingress rule in front of `/trax` specifically. This package
ships no `[Authorize]`, no `AuthorizeView` and no `RequireAuthorization()`, and it will not
warn you that you skipped this.

**A built-in authentication model** has never been weighed on its merits. It was simply never
built. The argument against one is the argument against any framework-owned auth: it would
have to ship a user store, which duplicates the host's, or consume the host's claims through
a configuration surface that ends up no smaller than the `[Authorize]` the host can already
write. That is an argument for the gate belonging to the host. It is not an argument for
shipping with the gate open and saying nothing, which is what this package does today. The
option stays open.

## Consequences

**`UseTraxDashboard()` mutates the host's application.** It calls `UseStaticFiles()`,
`UseAntiforgery()` and `MapStaticAssets()`, maps Razor components with the interactive server
render mode, and writes the route prefix and the environment name into the shared
`DashboardOptions` singleton, plus the title when one is passed. Those are side effects on
somebody else's application, and a host that already calls that middleware gets it twice.

**The mount path is fixed at `/trax`.** Every page carries a hardcoded `@page "/trax/..."`
template, `Routes.razor` is a plain `<Router>` over the assembly, and
`MapRazorComponents<App>()` applies no prefix, so the pages sit at `/trax` whatever
`UseTraxDashboard()` is passed. `DashboardOptions.RoutePrefix` is read in exactly one place,
`DashboardSidebar`, to build the navigation links. `UseTraxDashboard("/admin")` therefore
moves the links and not the pages, and every link 404s. Blazor route templates are
compile-time constants, so making the prefix real means changing how the pages are routed,
not changing this method.

**The host must be a Blazor-capable ASP.NET Core app.** Interactive server components need a
circuit, which means SignalR and sticky sessions if the host scales out. A pure Web API
host cannot take the dashboard without becoming something else.

**Radzen is a transitive dependency of every consumer** that mounts it, and its component
styles land in the host's static asset pipeline.

**Two ordering rules, both enforced by refusal.** `AddTrax()` must come before
`AddTraxDashboard()`, which checks for the `TraxMarker` and throws naming the call to add;
and `AddTraxDashboard()` must come before `UseTraxDashboard()`, which resolves
`DashboardOptions` through `GetRequiredService` and throws when nothing registered it. Both
refuse rather than silently changing behaviour. Only the first is the shape `api/0002`
describes, which is about reading the `IServiceCollection` during registration; the second
resolves from a built provider, where asking is safe by construction.

**The `TraxMarker` check is coarser than the dependency it guards.** The dashboard registers
no train discovery of its own and injects `ITrainDiscoveryService` into the Trains page and
the metadata detail page, but
that service comes from `AddMediator()`, while `TraxMarker` is registered by `AddTrax()`
unconditionally. A host calling `AddTrax(t => t.AddEffects(...))` and nothing else passes the
check and still has no discovery. What the check actually catches is a host that never called
`AddTrax()` at all, which is the common mistake and the one with the least legible failure.

## Exemplars

- `DashboardServiceExtensionsTests` pins part of the registration half: the options that land,
  the defaults (`/trax`), the scoped services, that the dashboard registers **no** train
  discovery of its own, and that the `TraxMarker` precondition throws with a message naming
  the call to add.

Not covered: four things, in descending order of reach.

**The dashboard applies no authorization, and nothing asserts that the host must supply it.**
An unguarded host serves `/trax` to anyone who can reach the port, and no guard, test or
startup check says so. Closing that is a product decision, not a test gap.

**`RoutePrefix` does not move the pages**, and nothing fails when the sidebar and the routes
disagree. The tests set a custom prefix and assert it reaches `DashboardOptions`, which is the
option plumbing working exactly as designed; nothing asserts where a page is then served, so
the divergence stays invisible until a consumer passes an argument and clicks a link. There is
a second way in: `UseTraxDashboard()` writes its prefix argument unconditionally, so a prefix
set through `AddTraxDashboard(o => ...)` is silently overwritten by a bare `UseTraxDashboard()`.

**Nothing asserts what `UseTraxDashboard()` does to the host's pipeline.** A change to the
middleware it adds, or to the order it adds it in, is invisible here and visible to every
consumer.

**Nothing asserts the `WebApplicationBuilder` overload of `AddTraxDashboard()`**, the one its
own documentation calls the recommended one. Every test builds a bare `ServiceCollection`, so
that overload's two mutations outside DI, `UseStaticWebAssets()` outside Development and an
in-memory source pushed to the top of the host's configuration root, are unasserted. Inside
the overload the tests do exercise, `AddRadzenComponents()` and
`AddRazorComponents().AddInteractiveServerComponents()` are unasserted too.

## Changelog

- **2026-09-11**: Corrected the claim that mounting into the host puts the dashboard behind
  the host's login. That holds only under a fallback authorization policy; the package applies
  none of its own. Reworked `## Considered options` around that and recorded a built-in auth
  model as still open rather than rejected.
- **2026-09-11**: Corrected the route prefix. The pages are hardcoded at `/trax` and
  `RoutePrefix` only moves the sidebar links.
- **2026-09-11**: Named the second ordering rule, Add before Use, and narrowed the
  `TraxMarker` precondition to what it actually catches, which is not the discovery dependency
  it was said to guard.
- **2026-09-11**: Widened `Not covered:` to the untested `WebApplicationBuilder` overload and
  the two product gaps above.
- **2026-09-11**: Recorded.
