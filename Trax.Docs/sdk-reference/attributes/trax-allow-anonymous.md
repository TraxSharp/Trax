---
layout: default
title: TraxAllowAnonymous
description: Reference for TraxAllowAnonymous, which declares a GraphQL-exposed train or query model deliberately public, its effect by placement and startup checks.
parent: Attributes
grand_parent: SDK Reference
nav_order: 2
---

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible. See [API Security](/docs/api-security).

# TraxAllowAnonymous

Declares a GraphQL-exposed surface intentionally public. Every `[TraxQuery]`/`[TraxMutation]` train and every `[TraxQueryModel]` entity on an open endpoint must declare either [`[TraxAuthorize]`](/docs/sdk-reference/attributes/trax-authorize) or this attribute, so anonymous access is always a deliberate choice rather than a forgotten gate.

## Signature

```csharp
namespace Trax.Effect.Attributes;

[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Method,
    AllowMultiple = false,
    Inherited = true
)]
public sealed class TraxAllowAnonymousAttribute : Attribute
{
    public TraxAllowAnonymousAttribute();
}
```

## Effect by placement

| Placed on | Effect |
|-----------|--------|
| A `[TraxQuery]`/`[TraxMutation]` train | Satisfies the exposure check. It adds no runtime gate and removes none: train authorization is enforced imperatively, and a train with neither attribute has no per-train requirement. |
| A `[TraxQueryModel]` entity | Opens the entity to unauthenticated reads. A navigation to an entity carrying `[TraxAuthorize]` still enforces that entity's gate. |
| A resolver method | Declares that one field public. Needed when the field's parent type has no gate to inherit. Under a role-gated parent it means any authenticated caller rather than only the role. |

## Startup checks

| Situation | Result |
|-----------|--------|
| The surface also carries `[TraxAuthorize]`, directly or by inheritance | Fails startup: the two contradict each other |
| The GraphQL endpoint is gated with `RequireAuthorization()` | Fails startup on any exposed surface: the endpoint rejects anonymous callers before the surface is reached, so the attribute could never take effect |
| An exposed surface on an open endpoint carries neither attribute | Fails startup, naming the type |

## Example

```csharp
using Trax.Effect.Attributes;

[TraxQuery(Namespace = "public")]
[TraxAllowAnonymous]
public class LookupAnnouncementTrain
    : ServiceTrain<LookupAnnouncementInput, AnnouncementOutput>, ILookupAnnouncementTrain
{
    protected override Task<Either<Exception, AnnouncementOutput>> Junctions() =>
        Chain<LookupAnnouncementJunction>().Resolve();
}
```

See [Anonymous Access via TraxAllowAnonymous](/docs/authorization#anonymous-access-via-traxallowanonymous) and [Required Exposure Posture](/docs/authorization#required-exposure-posture).

## Package

```
dotnet add package Trax.Effect
```
