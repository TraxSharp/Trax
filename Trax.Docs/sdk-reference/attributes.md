---
layout: default
title: Attributes
description: Index of every attribute Trax reads, in Trax.Effect.Attributes, grouped by authorization, data, execution, GraphQL exposure and framework.
parent: SDK Reference
nav_order: 0.6
has_children: true
---

# Attributes

Every attribute Trax reads, and what each one changes. They all live in `Trax.Effect.Attributes`, in the Trax.Effect package, so a train library can carry them without referencing the API, scheduler or GraphQL packages that act on them.

```csharp
using Trax.Effect.Attributes;

[TraxMutation]
[TraxAuthorize(Roles = "Admin")]
[TraxConcurrencyLimit(10)]
public class RefundOrderTrain : ServiceTrain<RefundInput, RefundResult>, IRefundOrderTrain { ... }

public record RefundInput(long OrderId, [property: TraxSensitive] string ApprovalCode);
```

## Authorization

| Attribute | Targets | Description |
|-----------|---------|-------------|
| [TraxAuthorize](/docs/sdk-reference/attributes/trax-authorize) | Class, interface, method | Requires an authenticated caller, a policy, or a role. Policies AND, roles OR. |
| [TraxAllowAnonymous](/docs/sdk-reference/attributes/trax-allow-anonymous) | Class, interface, method | Declares a GraphQL-exposed surface intentionally public |

## Data

| Attribute | Targets | Description |
|-----------|---------|-------------|
| [TraxSensitive](/docs/sdk-reference/attributes/trax-sensitive) | Property, field, parameter | Masks a value in the stored input and output, junction logs and lifecycle hooks |

## Execution

| Attribute | Targets | Description |
|-----------|---------|-------------|
| [TraxRemote](/docs/sdk-reference/attributes/trax-remote) | Class | Sends the train's queued runs to the first remote submitter |
| [TraxConcurrencyLimit](/docs/sdk-reference/mediator-api/concurrency-limiting#attribute) | Class | Caps concurrent direct runs of the train |

## GraphQL exposure

| Attribute | Targets | Description |
|-----------|---------|-------------|
| [TraxQuery / TraxMutation](/docs/sdk-reference/graphql-api/trax-graphql-attribute) | Class | Exposes a train as a GraphQL query or mutation |
| [TraxBroadcast](/docs/sdk-reference/graphql-api/trax-broadcast-attribute) | Class | Publishes the train's lifecycle events to GraphQL subscriptions |
| [TraxQueryModel](/docs/sdk-reference/graphql-api/query-models) | Class | Exposes an EF entity as a paged, filterable GraphQL query |

## Framework

| Attribute | Targets | Description |
|-----------|---------|-------------|
| [Inject](/docs/sdk-reference/attributes/inject) | Property | Property injection for `ServiceTrain`'s own framework services. Not for junction or train dependencies. |
