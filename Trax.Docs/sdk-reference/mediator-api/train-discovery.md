---
layout: default
title: TrainDiscovery
parent: Mediator API
grand_parent: SDK Reference
nav_order: 3
---

# TrainDiscovery

`ITrainDiscoveryService` scans the DI container for all registered `IServiceTrain<TIn, TOut>` implementations and returns structured metadata about each one. It lives in `Trax.Mediator` and is used by both the Dashboard (train listing) and the API layer (input schema generation, train lookup by name).

Registered automatically by `AddServiceTrainBus()` as a singleton.

## ITrainDiscoveryService

```csharp
public interface ITrainDiscoveryService
{
    IReadOnlyList<TrainRegistration> DiscoverTrains();
}
```

### DiscoverTrains

Returns all discovered train registrations. Results are cached after the first call — subsequent calls return the same list.

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

## How Discovery Works

1. Iterates every `ServiceDescriptor` in `IServiceCollection`.
2. For each descriptor, checks whether the service type (or any of its interfaces) is a closed generic of `IServiceTrain<,>`.
3. Skips concrete-type registrations that come from the dual-registration pattern (`AddScopedTraxRoute` registers both `TImplementation` and `TService`).
4. Extracts `InputType` and `OutputType` from the generic arguments.
5. **Deduplicates by `InputType`**: when multiple registrations share the same input type (interface + concrete from dual-registration), prefers the interface as `ServiceType` and the concrete class as `ImplementationType`.
6. Reads `[TraxAuthorize]` attributes from the implementation type and extracts policy and role requirements into `RequiredPolicies` and `RequiredRoles`.
7. Caches the result — the list is computed once and reused for the lifetime of the service.

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
