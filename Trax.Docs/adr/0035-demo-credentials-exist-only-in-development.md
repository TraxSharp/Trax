---
authors: [Theauxm]
repos: [api, samples]
areas: [platform]
status: accepted
---

# A demo credential carries the marker and exists only in Development

Every credential the Trax samples and templates publish (a plaintext API key, a JWT signing
key) is registered only when `IHostEnvironment.IsDevelopment()`, and every published API key
contains `do-not-use-in-production`. Trax.Api refuses to start a host outside Development when
a registered plaintext key carries that marker. The samples are copied into real hosts, and a
published key that is live in Production is a credential anyone who read the repository holds.

## Status

**Accepted.**

## Considered options

**Register the demo credentials everywhere and rely on the README warning.** That was the
state before this decision: the GameServer sample registered its HS256 signing keys in every
environment, so anyone could mint a Player token for a copied host, and four samples used keys
(`alice-key`) the Api's startup check cannot recognise.

**Rely on the marker alone.** The Api check only sees plaintext keys passed to
`AddTraxApiKeyAuth(keys => ...)`. It cannot see a resolver's keys, a JWT signing key, or a host
built against an Api release that predates the check, so the environment gate is what holds in
every case, and the marker is the second line for the copy that drops the gate.

**Ship no credential at all.** Every sample would need an identity provider before its first
request, which defeats a runnable sample.

## Consequences

Each sample host carries `Properties/launchSettings.json` with `ASPNETCORE_ENVIRONMENT=Development`,
so `dotnet run` behaves as before. A host started any other way has no demo credential: most
serve every `[TraxAuthorize]` operation as refused, and a host that calls
`RequireAuthorization()` with no other scheme refuses to start.

## Exemplars

**Enforced elsewhere:** `DemoKeysCarryTheMarkerTests` in Trax.Samples' `Tests.Meta` resolves
every key handed to `AddTraxApiKeyAuth` under `samples/` and `templates/`, inline or through a
resolver's dictionary, and fails on one without the marker. `DemoCredentialsEnvironmentTests`
in Trax.Samples.GameServer.E2E starts the GameServer API in Production and asserts neither the
API-key scheme nor the two demo JWT schemes is registered. `TemplateEnvironmentTests` in
Trax.Samples.Templates.Tests pins the same for the templates (samples/0003). The Api's startup
check is the marker's other half, on its `fix/demo-key-outside-development` branch.

Not covered: only the GameServer and the templates are started in Production by a test; the
other samples' `IsDevelopment()` gates are held by review. JWT signing keys carry no marker,
because nothing inspects them.

## Changelog

- **2026-09-28**: Recorded.
