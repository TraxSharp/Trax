---
layout: default
title: TraxAuthorize
description: "Reference for TraxAuthorize, which declares who may run a train, read a query model or call a field: how attributes combine, where enforced, startup checks."
parent: Attributes
grand_parent: SDK Reference
nav_order: 1
---

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible. See [API Security](/docs/api-security).

# TraxAuthorize

Declares who may run a train, read a query model, or call a GraphQL field. It is Trax's own authorization vocabulary: the same attribute and the same rules apply on a train, an entity, an interface and a resolver method, whatever GraphQL server sits underneath.

## Signature

```csharp
namespace Trax.Effect.Attributes;

[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Method,
    AllowMultiple = true,
    Inherited = true
)]
public class TraxAuthorizeAttribute : Attribute
{
    public TraxAuthorizeAttribute();
    public TraxAuthorizeAttribute(string policy);

    public string? Policy { get; init; }
    public string? Roles { get; init; }
}
```

| Member | Type | Description |
|--------|------|-------------|
| `TraxAuthorizeAttribute()` | | A bare gate: the caller must be authenticated, nothing more |
| `TraxAuthorizeAttribute(string policy)` | | Sets `Policy` |
| `Policy` | `string?` | The name of an ASP.NET Core authorization policy the caller must pass |
| `Roles` | `string?` | A comma-separated list of roles; the caller must hold at least one. Exact, case-sensitive match. |

## How attributes combine

| Attribute(s) | The caller must |
|--------------|-----------------|
| `[TraxAuthorize]` | Be authenticated |
| `[TraxAuthorize("P")]` | Pass policy `P` |
| `[TraxAuthorize(Roles = "A,B")]` | Hold role `A` or role `B` |
| `[TraxAuthorize("P1")] [TraxAuthorize("P2")]` | Pass `P1` and `P2`: policies AND across attributes |
| `[TraxAuthorize(Roles = "A")] [TraxAuthorize(Roles = "B")]` | Hold `A` and `B`: each attribute is a requirement of its own. List both in one attribute (`Roles = "A,B"`) for either |
| `[TraxAuthorize("P", Roles = "A")]` | Pass `P` and hold `A` |

On a train, attributes on the class, its base classes and every interface it implements are collected together, so `[TraxAuthorize("Admin")]` on `IDeleteUserTrain` gates `DeleteUserTrain` even when the class carries none. Collected together, they still combine as above: class `Roles = "Admin"` and interface `Roles = "Support"` require both. Discovery hands the requirement over as [`TrainRegistration.RequiredRoleSets`](/docs/sdk-reference/mediator-api/train-discovery#properties), one set per attribute.

## Where it is enforced

| Placed on | Enforced by |
|-----------|-------------|
| A train class or its interface | `ITrainExecutionService.RunAsync` and `QueueAsync`, before the input is read, through the registered `ITrainAuthorizationService` (Trax.Api registers one). Failure is `TrainAuthorizationException` with the public message `"Not authorized."`, `TRAX_AUTHORIZATION` over GraphQL. |
| A `[TraxQueryModel]` entity | HotChocolate's `@authorize` directive on the generated type and its entry field, so the gate holds through navigations, filters and sorts too |
| A resolver method, or an `[ExtendObjectType]` class | An `@authorize` directive on the field, or on every field the extension contributes. It does not re-gate the type being extended. |

A train run through `ITrainBus` in-process, by the scheduler, or inside an [ITrustedExecutionScope](/docs/sdk-reference/mediator-api/i-trusted-execution-scope) is not checked: authorization is enforced once, where a caller submits work.

## Startup checks

- A train with `[TraxAuthorize]` and no `ITrainAuthorizationService` registered stops the host at startup, unless the mediator was configured with `AllowMissingAuthorizationService()`. At run time the same case throws `TrainAuthorizationNotConfiguredException` outside a trusted scope.
- An empty or whitespace `Policy`, or a `Roles` list with no non-empty entry, fails at startup.
- A query model naming a policy that `AddAuthorization` never registered fails at startup. A train naming one does not: every call to it fails at request time with a masked `"Unexpected Execution Error"`, so register every policy a train names.
- A surface carrying both `[TraxAuthorize]` and [`[TraxAllowAnonymous]`](/docs/sdk-reference/attributes/trax-allow-anonymous) fails at startup.
- A surface declaring its posture with HotChocolate's `[Authorize]` or `[AllowAnonymous]` instead fails at startup, naming the Trax replacement.

## Example

```csharp
using Trax.Effect.Attributes;

[TraxMutation]
[TraxAuthorize("MustBeInternal")]
[TraxAuthorize(Roles = "Admin,Manager")]
public class RefundOrderTrain : ServiceTrain<RefundInput, RefundResult>, IRefundOrderTrain
{
    protected override Task<Either<Exception, RefundResult>> Junctions() =>
        Chain<IssueRefund>().Resolve();
}
```

The caller must pass `MustBeInternal` and hold `Admin` or `Manager`.

See [Authorization](/docs/authorization) for the full model, including the exposure posture every GraphQL surface must declare, and the [Auth sample](/docs/samples/auth) for each form on a running host.

## Package

```
dotnet add package Trax.Effect
```
