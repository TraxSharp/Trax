---
layout: default
title: TrainDiscovery
description: Reference for ITrainDiscoveryService and TrainRegistration, which list every registered service train for the dashboard and the API.
parent: Mediator API
grand_parent: SDK Reference
nav_order: 3
---

# TrainDiscovery

`ITrainDiscoveryService` scans the DI container for all registered `IServiceTrain<TIn, TOut>` implementations and returns structured metadata about each one. It lives in `Trax.Mediator` and is used by both the Dashboard (train listing) and the API layer (input schema generation, train lookup by name).

Registered automatically by `AddMediator()` as a singleton.

## ITrainDiscoveryService

```csharp
public interface ITrainDiscoveryService
{
    IReadOnlyList<TrainRegistration> DiscoverTrains();
}
```

### DiscoverTrains

Returns one registration per train. Two trains that take the same input type are both listed. Results are cached after the first call, so subsequent calls return the same list.

Since Trax.Mediator 1.23.2 discovery lists every train; before it, it listed one per input type. Trains that used to be hidden behind another train with the same input type now appear in `getTrains`, in the startup checks, and in runs by name. That includes Trax.Scheduler's own `JobDispatcherTrain` and `ManifestManagerTrain`.

**Returns**: `IReadOnlyList<TrainRegistration>`

**Throws**: `TrainException` when two different classes are registered under one class service type, such as `AddTransient<BaseTrain, A>()` and `AddTransient<BaseTrain, B>()`. A train is found by the name of the type it is registered under, and the container runs only the last registration, so the train that name describes would not be the train that runs. The same class registered twice is listed once and is not refused. The startup checks call `DiscoverTrains`, so a host with such a registration fails to start:

```text
2 trains are registered under My.BaseTrain: My.A, My.B. A train is found by the name of the type it is registered under, and the container runs only the last registration, so the train that name describes would not be the train that runs. Register each train under its own interface or class.
```

## TrainRegistration

Represents a single discovered train in the DI container.

```csharp
public class TrainRegistration
{
    public required Type ServiceType { get; init; }
    public required Type ImplementationType { get; init; }
    public required Type InputType { get; init; }
    public required Type OutputType { get; init; }
    public required ServiceLifetime Lifetime { get; init; }

    public required string ServiceTypeName { get; init; }
    public required string ImplementationTypeName { get; init; }
    public required string InputTypeName { get; init; }
    public required string OutputTypeName { get; init; }

    public required IReadOnlyList<string> RequiredPolicies { get; init; }
    public required IReadOnlyList<string> RequiredRoles { get; init; }
    public IReadOnlyList<IReadOnlyList<string>> RequiredRoleSets { get; init; }
    public bool HasAuthorizeAttribute { get; init; }
    public bool HasAllowAnonymousAttribute { get; init; }
    public bool HasQueueSubjectKey { get; init; }

    public required bool IsQuery { get; init; }
    public required bool IsMutation { get; init; }
    public required bool IsBroadcastEnabled { get; init; }
    public string? GraphQLName { get; init; }
    public string? GraphQLDescription { get; init; }
    public string? GraphQLDeprecationReason { get; init; }
    public required GraphQLOperation GraphQLOperations { get; init; }
}
```

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `ServiceType` | `Type` | The interface or concrete type registered in DI (e.g. `IProcessOrderTrain`) |
| `ImplementationType` | `Type` | The concrete class (e.g. `ProcessOrderTrain`) |
| `InputType` | `Type` | The `TInput` generic argument from `IServiceTrain<TInput, TOutput>` |
| `OutputType` | `Type` | The `TOutput` generic argument |
| `Lifetime` | `ServiceLifetime` | DI lifetime (`Singleton`, `Scoped`, `Transient`) |
| `ServiceTypeName` | `string` | Friendly display name for `ServiceType` (e.g. `IServiceTrain<OrderInput, OrderResult>`) |
| `ImplementationTypeName` | `string` | Friendly display name for `ImplementationType` |
| `InputTypeName` | `string` | Friendly display name for `InputType` |
| `OutputTypeName` | `string` | Friendly display name for `OutputType` |
| `RequiredPolicies` | `IReadOnlyList<string>` | Authorization policy names from `[TraxAuthorize]` attributes on the implementation class. Empty if no auth required. |
| `RequiredRoles` | `IReadOnlyList<string>` | Every role named by any `[TraxAuthorize(Roles = "...")]` attribute, as declared. This is the union of the attributes, not the requirement: check `RequiredRoleSets`. Empty if no roles required. |
| `RequiredRoleSets` | `IReadOnlyList<IReadOnlyList<string>>` | The role requirement: one set per `[TraxAuthorize]` attribute that names roles, de-duplicated. The caller must hold at least one role of **every** set, so separate attributes combine with AND, as separate `[Authorize]` attributes do in ASP.NET Core. Class `Roles = "Admin"` and interface `Roles = "Support"` is two sets, and a Support-only caller is refused. Roles inside one attribute combine with OR. On a registration built by hand without it, or with it set empty while `RequiredRoles` names roles, it is `RequiredRoles` as a single set, so listed roles are never read as no requirement. Added in Trax.Mediator 1.25.0. |
| `HasAuthorizeAttribute` | `bool` | Whether the train carries any `[TraxAuthorize]` (including the bare form). |
| `HasAllowAnonymousAttribute` | `bool` | Whether the train carries `[TraxAllowAnonymous]`. On a GraphQL-exposed train this is the explicit "intentionally public" marker that satisfies the exposure check; it carries no runtime gate of its own. Mutually exclusive with `HasAuthorizeAttribute` on an exposed train. |
| `HasQueueSubjectKey` | `bool` | Whether the implementation overrides [`QueueSubjectKey`](/docs/core/trains-and-junctions#queuesubjectkey-serializing-work-that-touches-the-same-thing), directly or through a base class. Only such a train can stamp a subject key on its queue entries, so only its queued work can be serialized against other work for the same subject. The enqueue uses the same check to decide whether to ask the train for a key. True does not mean every entry has one: the override may return null for a given input. |
| `IsQuery` | `bool` | Whether the train has a `[TraxQuery]` attribute and will be exposed as a typed GraphQL query. |
| `IsMutation` | `bool` | Whether the train has a `[TraxMutation]` attribute and will be exposed as typed GraphQL mutation(s). |
| `IsBroadcastEnabled` | `bool` | Whether the train has a `[TraxBroadcast]` attribute and will broadcast lifecycle events to subscribers. |
| `GraphQLName` | `string?` | Custom name override from `[TraxQuery(Name = "...")]` or `[TraxMutation(Name = "...")]`. Null means auto-derived. |
| `GraphQLDescription` | `string?` | Description for the generated GraphQL fields. |
| `GraphQLDeprecationReason` | `string?` | If non-null, the generated fields are marked as deprecated. |
| `GraphQLOperations` | `GraphQLOperation` | Which mutation operations (Run, Queue, or both) to generate. Only applies when `IsMutation` is true. Defaults to `Run`. |

## How Discovery Works

1. Iterates every `ServiceDescriptor` in `IServiceCollection`.
2. For each descriptor, checks whether the service type (or any of its interfaces) is a closed generic of `IServiceTrain<,>`.
3. Pairs each train's two registrations. `AddScopedTraxRoute` registers both `TImplementation` and `TService`, and the pair becomes one registration with the interface as `ServiceType` and the class as `ImplementationType`. The interface is the train's own (the one deriving from `IServiceTrain<,>`, as [AddMediator](/docs/sdk-reference/configuration/add-mediator#how-discovery-works) selects it), and the two are paired only when resolving the interface yields that class: the interface's descriptor names the class, or no other registered class implements the interface. A class registered with no interface of its own is listed under the class, and two different classes registered under the same class service type are refused (see **Throws** above).
4. Extracts `InputType` and `OutputType` from the generic arguments of `ServiceType`.
5. Lists every train, including trains that share an input type. Pairing is per train, never per input type, so a registration's requirements and attributes are always read from the class its `ServiceType` resolves to.
6. Reads `[TraxAuthorize]` attributes from the implementation type and extracts policy and role requirements into `RequiredPolicies`, `RequiredRoles` and `RequiredRoleSets` (one set per attribute), and sets `HasAuthorizeAttribute`. Reads `[TraxAllowAnonymous]` (across the base chain and interfaces) into `HasAllowAnonymousAttribute`. Discovery is permissive; the mutual-exclusion and exposure-posture checks run at host startup.
6b. Reads `[TraxQuery]` and `[TraxMutation]` attributes from the implementation type and populates `IsQuery`, `IsMutation`, `GraphQLName`, `GraphQLDescription`, `GraphQLDeprecationReason`, and `GraphQLOperations`.
6c. Reads the `[TraxBroadcast]` attribute and populates `IsBroadcastEnabled`.
6d. Checks whether the implementation type overrides `QueueSubjectKey` and sets `HasQueueSubjectKey`.
7. Caches the result. The list is computed once and reused for the lifetime of the service.

## Example

```csharp
public class TrainListController(ITrainDiscoveryService discovery) : ControllerBase
{
    [HttpGet("trains")]
    public IActionResult GetTrains()
    {
        var trains = discovery.DiscoverTrains();

        return Ok(trains.Select(t => new
        {
            t.ServiceTypeName,
            t.ImplementationTypeName,
            t.InputTypeName,
            t.OutputTypeName,
            Lifetime = t.Lifetime.ToString()
        }));
    }
}
```

## Package

```
dotnet add package Trax.Mediator
```
