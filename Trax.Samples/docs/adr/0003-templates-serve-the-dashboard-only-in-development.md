---
authors: [Theauxm]
areas: [samples]
status: accepted
---

# The templates serve the dashboard and the demo key only in Development

The `trax-hub` and `trax-scheduler` templates mount the dashboard, and the `trax-hub` and
`trax-api` templates register the plaintext demo API key, only when
`IHostEnvironment.IsDevelopment()`, and declare the dashboard's posture as
`AllowAnonymousDashboard()` inside that check and nowhere else. Every train and query model in
the `trax-hub` and `trax-api` templates carries `[TraxAuthorize(Roles = "User")]`, the role the demo key holds,
and none is anonymous. `dotnet run` starts in Development through each template's
`launchSettings.json`, so the out-of-the-box experience is unchanged once the demo key is
sent; a scaffold started any other way has no dashboard and no credential until its author
adds real ones, so every operation it serves is refused.

## Status

**Accepted.**

## Considered options

**Gate the template's dashboard with the demo key.** It would keep the dashboard in every
environment, but the key is published in the template itself, so a gate that accepts it is no
gate. A scaffold is started as-is more often than it is read.

**Ship real authentication in the template.** A user store, a login page and a scheme are the
host's choice, and any one the template picked would be the wrong one for most scaffolds. The
templates stay minimal and say in `Program.cs` where the gate goes.

**Leave it, and rely on `UseTraxDashboard()` refusing to start without a posture**
(dashboard/0002). That covers the dashboard once the templates move to that release, not the
demo key, and not the templates already published against earlier Trax.Dashboard versions.
The environment check works against every Trax.Dashboard version, so it does not wait for a
release. Since that release the templates need a posture anyway, and declaring
`AllowAnonymousDashboard()` only inside the Development check keeps the dashboard closed
everywhere else.

**Declare a posture in every environment** (`AllowAnonymousDashboard()` in Development,
`RequirePolicy` elsewhere), as the getting-started page does. The template would then mount a
dashboard in Production gated by a policy no scaffold can satisfy: with no authentication
scheme registered, every request to it is a 500, which invites deleting the gate to make it
work.

**Keep the template operations anonymous, since the demo key already goes away.** Removing
a credential gates nothing when the operations never asked for one: a scaffold run in
Production without the key still ran `dispatch.helloWorld` for anyone. The key and the
operation gate only work as a pair, and whoever copies a template train copies its attribute
with it.

**Leave the `Lookup` query anonymous as a read-only example.** It returns stub data, but the
shape it teaches is a lookup by ID, and a copy of it pointed at real rows is a read of anyone's
record by anyone. The template has no operation for which anonymous access is the right
answer, so it shows none.

## Consequences

A scaffold run with `ASPNETCORE_ENVIRONMENT` unset (a container, `dotnet Hub.dll`) starts in
Production, and `/trax` is a 404 there. That is the intended surprise: the fix is a posture in
`Program.cs`, not a different environment variable.

`trax generate` scaffolds from `trax-hub`, so a generated project inherits this.

## Exemplars

- `ScaffoldedTemplateTests` packs the templates, scaffolds each one outside the repo, builds
  it, and starts the built application through `dotnet run` in Development and with
  `ASPNETCORE_ENVIRONMENT=Production`, pinning that `/trax` is served only in Development.
- `TemplateEnvironmentTests` starts each template host in Production and in Development, and
  pins that `/trax` is served only in Development, that the demo key's scheme is registered
  only there, that an anonymous `dispatch.helloWorld`, `discover.lookup` or notes query is
  refused, and that the demo key runs them in Development.

Not covered: the tests scaffold from a package packed in this repository, not from the
published `Trax.Samples.Templates`. Nothing fails when a new template operation is added with `[TraxAllowAnonymous]` or with no attribute;
only the three operations named above are pinned.

## Changelog

- **2026-10-03**: Amended: the templates declare `AllowAnonymousDashboard()` inside the
  Development check, because Trax.Dashboard 1.16 refuses to start a dashboard with no posture.
  `ScaffoldedTemplateTests` added as an exemplar.

- **2026-09-27**: Amended: the template operations carry `[TraxAuthorize]`. The first version
  claimed every `[TraxAuthorize]` operation was refused outside Development while every
  template operation was `[TraxAllowAnonymous]`, so a Production scaffold still ran trains
  anonymously.
- **2026-09-27**: Recorded.
