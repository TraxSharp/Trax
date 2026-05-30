# Changelog

All notable changes to this project will be documented in this file.

## Unreleased

### Added

- `Trax.Api.Auth` package: `TraxPrincipal` record, `ITraxPrincipalResolver<T>` generic resolver, `TraxAuthClaimTypes` constants (`trax:principal-id`, `trax:principal-type`), and `TraxPrincipalExtensions` for `TraxPrincipal` ↔ `ClaimsPrincipal` conversion. Core abstraction for all current and future Trax authentication schemes.
- `AddTraxPrincipalAccessor()` extension: registers `TraxPrincipal` as a scoped DI service. Junctions and other consumers inject `TraxPrincipal` directly without touching `IHttpContextAccessor`. Resolves from the current request's `ClaimsPrincipal`; throws `TraxPrincipalNotAvailableException` when no authenticated Trax principal is on the execution context (anonymous request, scheduler path, or background service). Called automatically by every Trax auth scheme's `Add*` extension.
- `Trax.Api.Auth.ApiKey` package: `ApiKeyAuthHandler`, `ApiKeyAuthenticationOptions`, `ApiKeyDefaults`, and `AddTraxApiKeyAuth` extension. Consolidates the duplicated handler previously copied into `Trax.Samples.GameServer`, `Trax.Samples.ChatService`, and `Trax.Samples.JobHunt`.
- `Trax.Api.GraphQL.Audit` package: request-level audit pipeline built on HotChocolate's `ExecutionDiagnosticEventListener`. Exposes `TraxAuditEntry`, `ITraxAuditSink`, `ITraxAuditRedactor`, `TraxAuditOptions`, and a fluent `AddAudit<TSink>()` on `TraxGraphQLBuilder`. Uses a bounded channel + background writer so the request path never blocks on the sink.
- `ConfigureFiltering()` on `TraxGraphQLBuilder` plus `AddCaseInsensitiveStringOperations()` on the new `TraxFilterBuilder`: opt-in case-insensitive string filter operators `icontains` and `ieq`. They fold with SQL `lower()` (not `ILIKE`), so they work across every provider including InMemory and stay sargable against a `lower(col)` index. Registered on the filter convention, so they apply to every string filter input, including `ExposeAs`-projected and custom `AddFilterType` inputs. Stock filtering stays the default and the case-sensitive `contains`/`eq` are unchanged.

### Security

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [SECURITY-DISCLAIMER.md](SECURITY-DISCLAIMER.md).
