---
title: ITrainDiscoveryService
description: Reference for ITrainDiscoveryService and TrainRegistration, which list every registered service train for the dashboard, the API and train lookup by name.
type: reference
area: mediator
parent: Mediator API
grand_parent: SDK Reference
nav_order: 2
package: Trax.Mediator
namespace: Trax.Mediator.Services.TrainDiscovery
source:
  - Trax.Mediator/src/Trax.Mediator/Services/TrainDiscovery/ITrainDiscoveryService.cs
  - Trax.Mediator/src/Trax.Mediator/Services/TrainDiscovery/TrainDiscoveryService.cs
  - Trax.Mediator/src/Trax.Mediator/Services/TrainDiscovery/TrainRegistration.cs
redirect_from:
  - /docs/sdk-reference/mediator-api/train-discovery
---

# ITrainDiscoveryService

Lists every `IServiceTrain<TIn, TOut>` registered in the service collection, one
`TrainRegistration` per train. The dashboard lists trains from it, the API builds its schema from
it, and `ITrainExecutionService` looks trains up by name in it. `AddMediator()` registers the
default implementation, `TrainDiscoveryService`, as a singleton; it reads the `IServiceCollection`,
so it also works before the container is built, and is resolved through the interface.

## Signature

```csharp fragment
namespace Trax.Mediator.Services.TrainDiscovery;

public interface ITrainDiscoveryService
{
    IReadOnlyList<TrainRegistration> DiscoverTrains();
}
```

## Members

| Member | Description |
|--------|-------------|
| [`DiscoverTrains()`](#discovertrains) | Every registered train, as a cached list |
| [`TrainRegistration`](#trainregistration) | The record describing one train |

## DiscoverTrains

**Returns**: `IReadOnlyList<TrainRegistration>`, one per train. Two trains that take the same input
type are both listed. The list is computed on the first call and the same list is returned after.

**Throws**: `TrainException` when two different classes are registered under one class service
type, such as `AddTransient<BaseTrain, A>()` and `AddTransient<BaseTrain, B>()`. A train is found by
the name of the type it is registered under, and the container runs only the last registration, so
the train that name describes would not be the train that runs. The same class registered twice is
listed once and is not refused. The startup checks call `DiscoverTrains`, so a host with such a
registration fails to start:

```text
2 trains are registered under My.BaseTrain: My.A, My.B. A train is found by the name of the type it is registered under, and the container runs only the last registration, so the train that name describes would not be the train that runs. Register each train under its own interface or class.
```

Before Trax.Mediator 1.23.2 discovery listed one train per input type. Trains that were hidden
behind another train with the same input type now appear in `getTrains`, in the startup checks and
in runs by name, including Trax.Scheduler's own `JobDispatcherTrain` and `ManifestManagerTrain`.

## TrainRegistration

```csharp fragment
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
    public required bool IsRemote { get; init; }
    public string? GraphQLName { get; init; }
    public string? GraphQLDescription { get; init; }
    public string? GraphQLDeprecationReason { get; init; }
    public required GraphQLOperation GraphQLOperations { get; init; }
    public string? GraphQLNamespace { get; init; }
    public int? MaxConcurrentRun { get; init; }
}
```

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `ServiceType` | `Type` | The interface or class the train is registered under (for example `IProcessOrderTrain`). Its `FullName` is the train's canonical name |
| `ImplementationType` | `Type` | The concrete class (for example `ProcessOrderTrain`) |
| `InputType` | `Type` | The `TInput` of `IServiceTrain<TInput, TOutput>` |
| `OutputType` | `Type` | The `TOutput`; `Unit` for a train with no output |
| `Lifetime` | `ServiceLifetime` | The DI lifetime the train was registered with |
| `ServiceTypeName` | `string` | Display name of `ServiceType` (for example `IServiceTrain<OrderInput, OrderResult>`), the friendly name a train can also be looked up by |
| `ImplementationTypeName` | `string` | Display name of `ImplementationType` |
| `InputTypeName` | `string` | Display name of `InputType` |
| `OutputTypeName` | `string` | Display name of `OutputType` |
| `RequiredPolicies` | `IReadOnlyList<string>` | Policy names from `[TraxAuthorize]` attributes on the implementation class. Empty when none |
| `RequiredRoles` | `IReadOnlyList<string>` | Every role any `[TraxAuthorize(Roles = "...")]` attribute names, as declared: the union of the attributes, not the requirement. Empty when none |
| `RequiredRoleSets` | `IReadOnlyList<IReadOnlyList<string>>` | The role requirement, described below. Added in Trax.Mediator 1.25.0 |
| `HasAuthorizeAttribute` | `bool` | Whether the train carries any `[TraxAuthorize]`, the bare form included |
| `HasAllowAnonymousAttribute` | `bool` | Whether the train carries `[TraxAllowAnonymous]`. Carries no runtime gate of its own |
| `HasQueueSubjectKey` | `bool` | Whether the implementation overrides [`QueueSubjectKey`](/docs/mediator/queued-work#two-mutations-for-one-record), directly or through a base class |
| `IsQuery` | `bool` | Whether the train has `[TraxQuery]`, which exposes it as a typed GraphQL query |
| `IsMutation` | `bool` | Whether the train has `[TraxMutation]`, which exposes it as typed GraphQL mutations |
| `IsBroadcastEnabled` | `bool` | Whether the train has `[TraxBroadcast]`, which broadcasts its lifecycle events to subscribers |
| `IsRemote` | `bool` | Whether the implementation class has `[TraxRemote]`. A route set with `ForTrain<T>()` on the scheduler builder takes precedence; with no remote submitter configured the train runs locally |
| `GraphQLName` | `string?` | The name from `[TraxQuery(Name = ...)]` or `[TraxMutation(Name = ...)]`; null means derived |
| `GraphQLDescription` | `string?` | The description of the generated GraphQL fields |
| `GraphQLDeprecationReason` | `string?` | When set, the generated fields are marked deprecated |
| `GraphQLOperations` | `GraphQLOperation` | Which mutation operations (Run, Queue, or both) are generated. Applies only when `IsMutation` is true. Defaults to `Run` |
| `GraphQLNamespace` | `string?` | The sub-namespace the train's GraphQL field is grouped under (for example `discover { alerts { field } }`) |
| `MaxConcurrentRun` | `int?` | The limit from `[TraxConcurrencyLimit]`, or null. `ConcurrentRunLimit<T>()` on the mediator builder takes precedence |

`RequiredRoleSets` holds one set per `[TraxAuthorize]` attribute that names roles, de-duplicated.
The caller must hold at least one role of every set, so separate attributes combine with AND, as
separate `[Authorize]` attributes do in ASP.NET Core; roles inside one attribute combine with OR.
Class `Roles = "Admin"` and interface `Roles = "Support"` is two sets, and a Support-only caller is
refused. On a registration built by hand without it, or with it set empty while `RequiredRoles`
names roles, it reads as `RequiredRoles` in a single set, so listed roles are never read as no
requirement.

On a GraphQL-exposed train, `HasAllowAnonymousAttribute` is the explicit "intentionally public"
marker that satisfies the exposure check, and it is mutually exclusive with
`HasAuthorizeAttribute`. `HasQueueSubjectKey` true means the train can stamp a subject key on its
queue entries, not that every entry has one: the override may return null for a given input. The
enqueue uses the same check to decide whether to ask the train for a key.

## Behavior

`DiscoverTrains` builds the list as follows:

1. Reads every `ServiceDescriptor` in the `IServiceCollection` and keeps those whose service type,
   or one of its interfaces, is a closed `IServiceTrain<,>`.
2. Pairs each train's two registrations. `AddScopedTraxRoute` registers both `TImplementation` and
   `TService`, and the pair becomes one registration with the interface as `ServiceType` and the
   class as `ImplementationType`. The interface is the train's own (the one deriving from
   `IServiceTrain<,>`, as [AddMediator](/docs/sdk-reference/configuration/add-mediator#how-discovery-works)
   selects it), and the two are paired only when resolving the interface yields that class: the
   interface's descriptor names the class, or no other registered class implements the interface.
   A class registered with no interface of its own is listed under the class, and two different
   classes registered under the same class service type are refused (see
   [DiscoverTrains](#discovertrains)).
3. Takes `InputType` and `OutputType` from the generic arguments of `ServiceType`.
4. Lists every train, including trains that share an input type. Pairing is per train, never per
   input type, so a registration's requirements and attributes are always read from the class its
   `ServiceType` resolves to.
5. Reads `[TraxAuthorize]` from the implementation type into `RequiredPolicies`, `RequiredRoles`
   and `RequiredRoleSets` (one set per attribute) and sets `HasAuthorizeAttribute`; reads
   `[TraxAllowAnonymous]`, across the base chain and interfaces, into `HasAllowAnonymousAttribute`.
   Discovery is permissive; the mutual-exclusion and exposure checks run at host startup.
6. Reads `[TraxQuery]` and `[TraxMutation]` into `IsQuery`, `IsMutation`, `GraphQLName`,
   `GraphQLDescription`, `GraphQLDeprecationReason`, `GraphQLOperations` and `GraphQLNamespace`;
   `[TraxBroadcast]` into `IsBroadcastEnabled`; `[TraxRemote]` into `IsRemote`;
   `[TraxConcurrencyLimit]` into `MaxConcurrentRun`.
7. Checks whether the implementation overrides `QueueSubjectKey` and sets `HasQueueSubjectKey`.
8. Caches the result for the lifetime of the service.

## Example

```csharp compile
[ApiController]
public class TrainListController(ITrainDiscoveryService discovery) : ControllerBase
{
    [HttpGet("trains")]
    public IActionResult GetTrains() =>
        Ok(discovery.DiscoverTrains().Select(t => new
        {
            t.ServiceTypeName,
            t.ImplementationTypeName,
            t.InputTypeName,
            t.OutputTypeName,
            Lifetime = t.Lifetime.ToString(),
        }));
}
```

## See also

- [Train discovery and routing](/docs/mediator/train-discovery), how discovery and lookup by name fit together
- [ITrainExecutionService](/docs/sdk-reference/mediator-api/i-train-execution-service), which looks trains up here
- [AddMediator](/docs/sdk-reference/configuration/add-mediator#how-discovery-works), which registers the trains
