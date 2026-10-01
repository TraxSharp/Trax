---
authors: [Theauxm]
areas: [graphql, platform]
status: accepted
---

# A misconfigured host fails at startup, not at request time

When a host wires Trax's GraphQL surface wrongly, it throws while starting, with a message
naming the call to change. It does not build a schema that looks fine and then fails on the
request that happens to touch the broken part.

**Seven** validators enforce it: `QueryModelAuthorizationSchemaValidator`,
`QueryModelAuthorizationValidator`, `TraxOperationsServiceValidator`,
`TraxSubscriptionAuthWiringValidator`, `TraxGraphQLAuthPolicyValidator`,
`TypeExtensionExposureValidator` and `TrainRegistrationOrderValidator`, which names a train
registered after `AddTraxGraphQL()`. `DemoApiKeyEnvironmentValidator` in `Trax.Api.Auth.ApiKey`
applies the same policy to a demo key outside Development. Each is a hosted service, so it runs
after the container is complete and sees the truth regardless of the order the host registered
anything in, and each names the call to change.

Each checks in `IHostedLifecycleService.StartingAsync` (through the `StartupGate` base), which
the host finishes for every hosted service before it calls any `StartAsync`. Kestrel and a
host's own workers start in `StartAsync`, so a refused host never binds a port or claims work,
even under `HostOptions.ServicesStartConcurrently`, where every `StartAsync` begins at once and
an ordinary `StartAsync` check would race them. A host that calls only `StartAsync` (a custom
`IHost`, a test harness) gets the same check from `StartAsync`, so skipping a lifecycle step does
not open the gate. Trax.Mediator's startup gates work the same way.

`QueryModelScalarCollectionIndexValidator` sits in the same folder and is **not** one of
them: it is advisory, logs a warning about a missing GIN index and never blocks startup. A
query-plan hint is not a misconfiguration. `GraphQLClientStartupValidator` in
`Trax.Api.GraphQL.Client.Trax` applies the same policy to outbound schema drift, which is a
different surface and is not counted here either.

## Status

**Accepted.**

## Considered options

**Throwing from the registration extension itself.** Simpler, and wrong for most of these
checks: `AddTraxGraphQL()` runs partway through the host's startup code, so anything it
concludes from the `IServiceCollection` is a statement about where the caller happens to be,
not about the finished container. See
[0002](./0002-reading-the-service-collection-is-order-dependent.md).

**Letting it fail at request time**, which is what happens by default. The failure arrives
masked as an "Unexpected Execution Error" on one operation, pointing at a resolver rather
than at the missing registration three files away in `Program.cs`. For the subscription
case it did not fail at all: HotChocolate's default interceptor accepted every
`connection_init`, so subscriptions silently stopped authenticating while HTTP kept working.

## Consequences

**The message is part of the contract.** A validator that throws without naming the call to
add is only marginally better than the request-time failure it replaced. `TraxOperationsServiceValidator`
names `ExposeOperationQueries` and the services to register; the subscription one names the
schemes that are registered and the supported way to supply an interceptor.

**Startup cost is paid only where there is something to check.**
`QueryModelAuthorizationSchemaValidator` materialises the whole schema at boot, so it is
registered only when the host has at least one `[TraxAuthorize]` or `[TraxAllowAnonymous]`
query model. A host with neither never pays for it.

**It checks both directions.** A gated entity that lost `@authorize` fails the host, and so
does an anonymous one that gained it. The escape hatch it closes is a consumer's
`ConfigureSchema` callback, which runs after Trax's own wiring and has full builder access by
design, so it is the one place Trax cannot control.

## Exemplars

- `StartupGateHostTests` starts real hosts, sequentially and with `ServicesStartConcurrently`:
  a refused host never starts a worker registered before Trax, and a train registered after
  `AddTraxGraphQL()` refuses the host naming it.
- `TraxOperationsServiceValidatorTests` pins the fail-fast behaviour for the operations
  surface, including the message.
- `QueryModelAuthorizeSchemaValidatorTests` drives the schema validator directly: it asserts
  the host throws naming the entity, the type or the entry field when a directive is stripped
  in each of the three ways, and does not throw when they are intact.
- [Registration Order](/docs/reference/registration-order) is the user-facing statement of
  what order matters and what happens when it is wrong.

Not covered:

- Nothing checks that a new piece of host configuration *has* a validator. Adding a surface
  that can be half-wired and forgetting the check is invisible to the build, and that is the
  failure mode this ADR is about.
- `QueryModelAuthorizeSchemaInvariantE2ETests.cs` covers only the passing direction. Its one
  explicit assertion is that the host is not null, which cannot fail, but the host comes from
  `await host.StartAsync()`, which runs the validator as a hosted service against the real
  built schema: a regression that made it throw on a correctly wired host does fail the test.
  What nothing covers end to end is the failing direction. The stripped-directive cases are
  driven against hand-built schemas in `QueryModelAuthorizeSchemaValidatorTests`, because the
  realistic bypasses are rejected by HotChocolate's own type-uniqueness check before the
  validator sees them. It also needs a live Postgres and fails rather than skipping without
  one.

## Changelog

- **2026-09-30**: The validators check in `StartingAsync`, so a refusal precedes Kestrel and the
  host's workers under concurrent start; `TrainRegistrationOrderValidator` added; the count
  corrected to seven, with `TypeExtensionExposureValidator`, which was missing.
- **2026-09-27**: `TraxSubscriptionAuthWiringValidator` is a backstop now, not an ordering
  check: [0006](./0006-one-socket-interceptor-composes-every-token-scheme.md) made subscription
  auth independent of registration order, so its message no longer names an order to fix.
- **2026-09-11**: Corrected the account of `QueryModelAuthorizeSchemaInvariantE2ETests`.
  Starting the host runs the validator, so the success path is covered; what is missing is
  the failing direction end to end.
- **2026-09-11**: Recorded.
