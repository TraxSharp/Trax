---
layout: default
title: TrainDiscovery
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

Returns all discovered train registrations. Results are cached after the first call, so subsequent calls return the same list.

**Returns**: `IReadOnlyList<TrainRegistration>`

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
| `RequiredRoles` | `IReadOnlyList<string>` | Role names from `[TraxAuthorize(Roles = "...")]` attributes. Empty if no roles required. |
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
3. Skips concrete-type registrations that come from the dual-registration pattern (`AddScopedTraxRoute` registers both `TImplementation` and `TService`).
4. Extracts `InputType` and `OutputType` from the generic arguments.
5. **Deduplicates by `InputType`**: when multiple registrations share the same input type (interface + concrete from dual-registration), prefers the interface as `ServiceType` and the concrete class as `ImplementationType`.
6. Reads `[TraxAuthorize]` attributes from the implementation type and extracts policy and role requirements into `RequiredPolicies` and `RequiredRoles`.
6b. Reads `[TraxQuery]` and `[TraxMutation]` attributes from the implementation type and populates `IsQuery`, `IsMutation`, `GraphQLName`, `GraphQLDescription`, `GraphQLDeprecationReason`, and `GraphQLOperations`.
6c. Reads the `[TraxBroadcast]` attribute and populates `IsBroadcastEnabled`.
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
