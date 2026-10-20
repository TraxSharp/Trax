---
title: API conventions
description: "Conventions for Trax's public API: the builder pattern and state markers, Add and Use extension naming, and reading the service collection safely."
type: reference
area: contributing
nav_order: 2
redirect_from:
  - /docs/reference/builder-pattern
  - /docs/reference/extension-method-naming
---

# API conventions

The shape a new public API in Trax takes: how a builder is structured, what an extension method
is called, and what a registration extension may and may not ask of the service collection.
Users see the result of these rules in [Registration Order](/docs/reference/registration-order).

## Builder pattern

Four builders configure the subsystems: `TraxEffectBuilder`, `TraxMediatorBuilder`,
`SchedulerConfigurationBuilder` and `TraxGraphQLBuilder`. They agree on the class structure and
on the `Func<TBuilder, TBuilder>` entry point, and they disagree on what `Build()` returns and
what it is the caller's job to register. Match the closest one when you add a builder method;
match `TraxMediatorBuilder` when you add a whole builder, because it is the one that does every
part of this the intended way.

### Entry point

The extension method takes `Func<TBuilder, TBuilder>`, not `Action<TBuilder>`. It creates the
builder, invokes the function, calls `Build()`, and returns the next state marker in the chain.

```csharp fragment
public static TraxBuilderWithEffects AddEffects(
    this TraxBuilder trax,
    Func<TraxEffectBuilder, TraxEffectBuilder> configure
)
```

`AddTrax` is the exception: it takes `Action<TraxBuilder>` because it is the root entry point
and returns `IServiceCollection` rather than a builder state type.

Provide a parameterless overload that calls the main one with an identity function.
`AddEffects()` and `AddScheduler()` have one. `AddMediator` does not: its second overload is
`params Assembly[]`, so `AddMediator()` compiles and reaches `Build()` with nothing to scan.
That is caught, not silent: `Build()` throws unless another subsystem contributed assemblies
first, in which case those are merged and scanned. A new builder should still prefer the
identity overload, so the mistake is unrepresentable rather than merely diagnosed.

### Class structure

Every builder is a `partial class` in its own directory, split one file per feature area:

| File | Holds |
|---|---|
| `Builder.cs` | State and constructor |
| `Builder.Build.cs` | Validation and the `Build()` method |
| `Builder.<Feature>.cs` | One feature area per file |

Methods return `this` for chaining. `TraxMediatorBuilder` and `TraxGraphQLBuilder` are the
reference implementations.

### Build()

`Build()` is `internal`, so the fluent chain is the only way to reach it. It holds the
build-time validation, which throws `InvalidOperationException` with a message that names the
misconfigured method, explains why it is wrong, and shows the fix. It never returns the parent
builder.

Three of the four return a configuration object and leave registration to the caller:
`TraxEffectBuilder.Build()` returns `TraxEffectConfiguration`, `TraxMediatorBuilder.Build()`
returns `MediatorConfiguration`, `TraxGraphQLBuilder.Build()` returns `GraphQLConfiguration`.

`SchedulerConfigurationBuilder.Build()` returns `void` and registers `SchedulerConfiguration`
and the scheduler's services itself, against the parent builder's `ServiceCollection`. That is
why `AddScheduler` returns the same `TraxBuilderWithMediator` it received rather than a new
marker: the scheduler is the last link in the chain, so there is no next state to promote to.
Do not copy the shape for a new subsystem. A `Build()` that returns the configuration keeps the
registration in the extension method where a reader looks for it.

### Configuration object

Named `{Subsystem}Configuration` and registered as a singleton, though where and how varies:

| Type | Setters | Registered by |
|---|---|---|
| `MediatorConfiguration` | `internal` | `AddMediator` |
| `SchedulerConfiguration` | mostly `internal`, a few hidden `public` | `SchedulerConfigurationBuilder.Build()` |
| `GraphQLConfiguration` | none, assigned in an `internal` constructor | `AddTraxGraphQL` |
| `TraxEffectConfiguration` | `public` | `AddTrax`, as `ITraxEffectConfiguration` |

`MediatorConfiguration` is the shape to copy: internal setters make it immutable from outside
the builder, which is what stops a consumer mutating resolved configuration at run time.
`SchedulerConfiguration` mostly follows it. Runtime changes go through the validated
`operations.config` path that the dashboard's server settings page also uses, and the scheduler
re-applies the `trax.scheduler_config` row that path writes to the singleton at startup and every few seconds while it runs. A few
setters that existing hosts and tests assign directly (the polling intervals, `MaxActiveJobs`,
`DefaultMaxRetries`, `DefaultRetryDelay`, `DefaultJobTimeout`, `ManifestManagerEnabled`) stay public but are hidden from
IntelliSense.
`TraxEffectConfiguration` has public setters with no reason behind them.
`GraphQLConfiguration` goes the other way and takes everything through its constructor, which is
`internal`: only `TraxGraphQLBuilder.Build()` creates one.

### State markers

`TraxBuilder` to `TraxBuilderWithEffects` to `TraxBuilderWithMediator`. All three expose
`ServiceCollection`, `HasDatabaseProvider` and `HasDataProvider`. `Root` is not part of that
set: `TraxBuilder` is the root and has none, `TraxBuilderWithMediator.Root` is `internal`, and
only `TraxBuilderWithEffects.Root` is public, because `AddMediator` needs it to read the
assemblies earlier subsystems contributed.

`HasDatabaseProvider`, `HasDataProvider` and `TraxBuilder.MediatorConfigured` are read-only
outside Trax. Build-time validation reads them to fail closed (the scheduler refuses to build
without a data provider, `AddStateMachines` refuses to run after `AddMediator`), so only the call
that earns a flag sets it: a data provider's `Use*` method, or `AddMediator`. The setters are
`internal`, visible to the Trax.Effect data provider assemblies and Trax.Mediator. A test that
needs a builder with a data provider calls a real one, `UseInMemory()` being the cheapest.

Extension methods target a specific marker, which is what enforces ordering at compile time
rather than at run time. `AddDataContextLogging()` is the same idea one level down: it is
declared on `TraxEffectBuilderWithData`, which only a data provider call returns.

#### Wrong-order overloads

A call made on the wrong marker would otherwise fail with CS1929, which names the marker types
and leaves the reader to work out the order from them. So a transition someone can get wrong also
gets an overload on the marker the call is wrongly made on, marked
`[Obsolete("Call X(...) before Y(...).", error: true)]` and `[EditorBrowsable(Never)]`. The
compiler then reports CS0619 with the instruction as its text:

```
error CS0619: 'BuilderOrderExtensions.AddMediator(TraxBuilder, params Assembly[])' is obsolete:
'Call AddEffects(...) before AddMediator(...). See https://traxsharp.net/docs/cross-cutting/troubleshooting#call-addeffects-before-addmediator'
```

Trax.Mediator's are in `BuilderOrderExtensions`:

| Wrong call | Error text |
|---|---|
| `AddMediator(...)` on `TraxBuilder`, before `AddEffects` | `Call AddEffects(...) before AddMediator(...).` |
| `AddMediator(...)` on `TraxBuilderWithMediator`, a second time | `AddMediator(...) is already called. Call it once and configure everything in that call.` |
| `AddStateMachines(...)` on `TraxBuilderWithMediator`, after `AddMediator` | `Call AddStateMachines(...) before AddMediator(...).` |

Trax.Effect.Data's is in its own `BuilderOrderExtensions`:

| Wrong call | Error text |
|---|---|
| `AddDataContextLogging(...)` on `TraxEffectBuilder`, before a data provider | `Call UsePostgres(...), UseSqlite(...) or UseInMemory(...) before AddDataContextLogging(...).` |

When you add one, take the real method's parameter list and names exactly, so a call that uses
named arguments or a lambda binds to the overload and reports the instruction rather than an
argument error. The receiver has to be a marker the real method does not accept, or the correct
order becomes ambiguous. The overload lives in the package that owns the later marker, because
that is the one that can name both types: `AddStateMachines` is Trax.Effect's, but the
`TraxBuilderWithMediator` overload is Trax.Mediator's, and its options parameter is
`Action<dynamic>` because Trax.Mediator does not reference the options type. The body throws and
never runs. `BuilderOrderDiagnosticsTests`, in Trax.Mediator and in Trax.Effect, compiles each wrong
order in memory and asserts the one error it produces, and compiles each right order and asserts none.

## Extension method naming

`services.AddTrax(trax => trax.AddEffects(...).AddMediator(...).AddScheduler(...))` is the
unified entry point. Everything else follows one of two prefixes.

| Prefix | Means | Examples |
|---|---|---|
| `Add*` | Registers services on the `IServiceCollection` | `AddTraxGraphQL`, `AddTraxWorker`, `AddTraxJobRunner` |
| `Use*` | Maps endpoints or components onto a built `WebApplication` | `UseTraxApiDashboard`, `UseTraxGraphQL` |

Every public extension method in an `Extensions/` folder contains `Trax` in its name.
`ExtensionMethodNamingTests`, which the `Tests.Meta` suite of each of the seven .NET package
folders carries, holds part of that: it parses the `.cs` files under an `Extensions/` folder in
that folder's source tree and flags a
`public static` method only when the name starts with `Add` or `Use` **and** the `this`
parameter is one of `IServiceCollection`, `IApplicationBuilder`, `IEndpointRouteBuilder`,
`WebApplication` or `WebApplicationBuilder`. A method on a Trax builder type, or one whose name
starts with anything else, is outside what it reads. Each folder's copy also carries its own
`KnownExceptions` list, so a name can be exempted with a justification rather than fixed.
Everything the check does not reach is held up by review.

The distinction matters because the two run at different times. An `Add*` method sees a
collection that is still being populated, so what it finds there answers "is this registered
*yet*", not "will this be registered". Reading the collection is not itself wrong; deriving
behaviour from the answer and saying nothing about it is.
[Reading the service collection](#reading-the-service-collection) has the three shapes that
are acceptable. A `Use*` method runs after the container is built and can resolve anything, which
is usually the better place to put the question.

## Reading the service collection

Reading the `IServiceCollection` inside a registration extension answers "is this registered
*yet*", not "will this be registered". The extension runs partway through the host's startup
code, so any behaviour derived from the answer changes with the caller's ordering.

That has shipped as a bug twice: once leaving an interceptor unable to activate (a 500 on every
request), and once leaving subscription auth unwired entirely, which accepted every connection
anonymously while HTTP kept working.

Inspecting the collection is not banned. Silence is. A site is acceptable only if it is:

1. **An idempotency guard**, asking "have I already registered my own thing?" It cannot change
   consumer-visible behaviour.
2. **A precondition that throws**, where ordering matters and the consumer is told how to fix it.
3. **A decision paired with a startup validator**: wire what you can, then assert from an
   `IHostedService`, where the container is complete, that the result is coherent, and throw
   naming the call to move.

Prefer restructuring so the question disappears. Registering a factory that resolves on first
use makes the ordering question moot rather than merely detected.

`NoSilentRegistrationOrderDependenceTests` fails the build on any new introspection site.
Making one safe and recording why is the way past it; raising its count is not.

## SDK Reference

> [Configuration](/docs/sdk-reference/configuration) | [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [AddMediator](/docs/sdk-reference/configuration/add-mediator) | [MediatorConfiguration](/docs/sdk-reference/mediator-api/mediator-configuration) | [UseTraxApiDashboard](/docs/sdk-reference/api-dashboard/use-trax-api-dashboard) | [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [Architecture guards](/docs/reference/architecture-guards)
