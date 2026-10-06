---
authors: [Theauxm]
repos: [api, samples]
areas: [platform]
status: accepted
---

# A demo credential carries the marker and exists only in Development

Every credential the Trax samples and templates publish (a plaintext API key, a JWT signing
key) is registered only when `IHostEnvironment.IsDevelopment()`, and every published API key
and JWT signing key contains `do-not-use-in-production`. Trax.Api refuses to start a host outside
Development when a registered plaintext API key or symmetric JWT signing key carries that marker. The samples are copied into real hosts, and a
published key that is live in Production is a credential anyone who read the repository holds.

## Status

**Accepted.**

## Considered options

**Register the demo credentials everywhere and rely on the README warning.** That was the
state before this decision: the GameServer sample registered its HS256 signing keys in every
environment, so anyone could mint a Player token for a copied host, and four samples used keys
(`alice-key`) the Api's startup check cannot recognise.

**Rely on the marker alone.** The Api check only sees plaintext keys passed to
`AddTraxApiKeyAuth(keys => ...)` and the symmetric signing keys a JWT bearer scheme's options hold
at startup, however `AddTraxJwtAuth` or the host set them. It cannot
see a resolver's keys, an asymmetric signing key, or a host built against an Api release that
predates the check, so the environment gate is what holds in
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
checks, one for API keys and one for symmetric JWT signing keys, are the marker's other half.

Not covered: only the GameServer and the templates are started in Production by a test; the
other samples' `IsDevelopment()` gates are held by review. The Samples guard resolves API keys
only, so a sample's JWT signing key that drops the marker is caught by review, not by a test.

## Changelog

- **2026-10-05**: The JWT check reads every bearer scheme's final options, so a marked key set
  through `CustomizeTokenValidation`, `CustomizeBearerOptions` or a host's own bearer scheme is
  refused too.
- **2026-10-05**: Extended to JWT signing keys. Trax.Api refuses a symmetric signing key that
  carries the marker outside Development, as it does an API key.
- **2026-09-28**: Recorded.
