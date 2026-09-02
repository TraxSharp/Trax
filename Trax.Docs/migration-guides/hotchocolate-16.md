# HotChocolate 16 Migration

`Trax.Api.GraphQL` and its companion packages now build against HotChocolate 16. Most of the
surface is unchanged, but HotChocolate 16 renamed several types, split its service containers,
and renamed four scalars. This page lists what a consuming host has to change.

## Update your HotChocolate references

Every HotChocolate package a host references must move to 16 in lockstep with Trax. Two package
changes matter:

| Package | Change |
|---|---|
| `HotChocolate.Execution` | Discontinued. Its types moved into `HotChocolate.Execution.Abstractions`, which `HotChocolate` already brings in. Delete the reference. |
| `HotChocolate.Execution.Projections` | New. `Trax.Api.GraphQL` depends on it; reference it directly only if your own code calls `AsSelector`. |

## Renamed types

| Before | After |
|---|---|
| `ISchema` | `ISchemaDefinition` |
| `IObjectType` | `IObjectTypeDefinition` |
| `IDirectiveCollection` | `IReadOnlyDirectiveCollection` (and `IDirective.Type.Name` becomes `IDirective.Definition.Name`) |
| `IOperationResult` | `OperationResult` |
| `IRequestContext` | `RequestContext` |
| `IDocumentValidatorContext` | `DocumentValidatorContext` |
| `IRequestExecutorResolver` | `IRequestExecutorProvider` for lookups, `IRequestExecutorManager` when you also evict |
| `resolver.GetRequestExecutorAsync(...)` | `provider.GetExecutorAsync(...)` |
| `schema.GetType<T>(name)` | `schema.Types.GetType<T>(name)` |

## Schema components no longer see your application services

HotChocolate 16 builds request interceptors, socket-session interceptors, diagnostic listeners
and error filters from a schema container that does not fall through to the application
container. A component that takes `IHttpContextAccessor`, an options monitor, or any host
registration now fails to activate with `Unable to resolve service for type ...`.

Bridge each dependency across:

```csharp
builder.AddApplicationService<IAuthenticationSchemeProvider>();
builder.AddHttpRequestInterceptor<MyInterceptor>();
```

The bridge resolves eagerly while the schema container is built, so bridging a service the host
did not register turns an optional dependency into a startup failure. Bridge conditionally when
the component itself is wired conditionally.

`IServiceProvider` and `IServiceScopeFactory` cannot be bridged: the schema container registers
its own and those win. A component that resolves services dynamically takes
`TraxApplicationServices` instead, which Trax registers and bridges for you.

```csharp
public sealed class MySocketInterceptor(TraxApplicationServices applicationServices)
    : DefaultSocketSessionInterceptor
{
    // applicationServices.Services is the host container, with its scoped registrations.
}
```

Trax wires its own interceptors this way already. You only need it for components you register
yourself.

## connection_init payloads

`IOperationMessagePayload.As<T>()` is gone. Read the raw JSON instead:

```csharp
var payload = init.Payload?.Deserialize<MyPayload>(
    new JsonSerializerOptions(JsonSerializerDefaults.Web)
);
```

Watch for a silent failure here: LanguageExt (a Trax dependency) supplies an unrelated
`As<T>()` extension, so the old call still compiles and returns null at runtime. Search for
`.As<` in your socket interceptors rather than trusting the compiler.

## Scalar renames change your SDL

Four built-in scalars were renamed. Any entity property of these CLR types produces a different
type name in the schema, which breaks generated clients until they are regenerated:

| CLR type | Before | After |
|---|---|---|
| `TimeSpan` | `TimeSpan` | `Duration` |
| `Uri` | `URL` | `URI` |
| `byte` | `Byte` | `UnsignedByte` |
| `sbyte` | `SignedByte` | `Byte` |

Trax's own admin schema exposes one of these (`operations.dispatch.delay` is a `TimeSpan`), so
regenerate any client bound to it.

## Server defaults that moved

| Setting | Change |
|---|---|
| Batching | Off by default. Enable with `ModifyServerOptions(o => o.Batching = AllowedBatching.All)`. |
| Document and operation caches | Sized through `ModifyOptions(o => o.OperationDocumentCacheSize = ...)` / `PreparedOperationCacheSize`, not `AddDocumentCache()` / `AddOperationCache()`. |
| Schema initialization | Eager by default. Remove `InitializeOnStartup()`; opt out with `ModifyOptions(o => o.LazyInitialization = true)`. |
| Parser limits | New caps on recursion depth, directives per location, and fragment visits. |
| Concurrency | New `MaxConcurrentExecutions` (default 64). |

Trax's own hardening defaults (execution depth, cost, per-request operation cap) are unchanged
and still applied by `AddTraxGraphQL`.

## Projection

`[UseProjection]` is deprecated upstream, and Trax no longer uses it: query-model fields project
through HotChocolate's execution-time projection subsystem instead. The behaviour a caller sees
is the same, with one improvement, which is that a hand-written `[ExtendObjectType]` resolver
now receives the parent properties it needs. See
[query models](/docs/sdk-reference/graphql-api/query-models#projection-and-hand-written-resolvers).

If you previously worked around that with `[IsProjected(true)]` on an entity property, delete
it. The key of a query model is now added automatically, and anything else is declared on the
resolver with `[Parent(requires: ...)]`, which is enforced by the
[architecture guards](/docs/reference/architecture-guards).

## SDK Reference

> [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [Query models](/docs/sdk-reference/graphql-api/query-models) | [Cross-schema data loaders](/docs/sdk-reference/graphql-api/cross-schema-data-loaders) | [Architecture guards](/docs/reference/architecture-guards)
