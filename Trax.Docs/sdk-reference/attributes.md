---
title: Attributes
description: "Index of every attribute Trax reads, all in Trax.Effect.Attributes, grouped by what they govern: authorization, data, execution and GraphQL exposure."
type: landing
area: trains
parent: SDK Reference
has_children: true
nav_order: 2
---

# Attributes

Every attribute Trax reads ships in the Trax.Effect package, in the `Trax.Effect.Attributes` namespace, so a train library can carry them without referencing the API, scheduler or GraphQL packages that act on them. The GraphQL exposure attributes are documented with the GraphQL API.

## Authorization

| Attribute | Targets | Description |
|-----------|---------|-------------|
| [TraxAuthorize](/docs/sdk-reference/attributes/trax-authorize) | Class, interface, method | Requires an authenticated caller, a policy, or a role. Policies AND, roles OR. |
| [TraxAllowAnonymous](/docs/sdk-reference/attributes/trax-allow-anonymous) | Class, interface, method | Declares a GraphQL-exposed surface intentionally public |
| [TraxAuthorization](/docs/sdk-reference/attributes/trax-authorization) | | Reads the two attributes above off a type or method |

## Data

| Attribute | Targets | Description |
|-----------|---------|-------------|
| [TraxSensitive](/docs/sdk-reference/attributes/trax-sensitive) | Property, field, parameter, enum, class, struct | Masks a value in the stored input and output, junction logs and lifecycle hooks; withholds a question's answers |

## Execution

| Attribute | Targets | Description |
|-----------|---------|-------------|
| [TraxRemote](/docs/sdk-reference/attributes/trax-remote) | Class | Sends the train's queued runs to the first remote submitter |
| [TraxConcurrencyLimit](/docs/sdk-reference/mediator-api/concurrency-limiting#attribute) | Class | Caps concurrent direct runs of the train |
| [Inject](/docs/sdk-reference/attributes/inject) | Property | Property injection for `ServiceTrain`'s own framework services |

## GraphQL exposure

| Attribute | Targets | Description |
|-----------|---------|-------------|
| [TraxQuery / TraxMutation](/docs/sdk-reference/graphql-api/trax-graphql-attribute) | Class | Exposes a train as a GraphQL query or mutation |
| [TraxBroadcast](/docs/sdk-reference/graphql-api/trax-broadcast-attribute) | Class | Publishes the train's lifecycle events to GraphQL subscriptions |
| [TraxQueryModel](/docs/sdk-reference/graphql-api/query-models) | Class | Exposes an EF entity as a paged, filterable GraphQL query |
| [TraxMachine](/docs/sdk-reference/statemachine-api/add-state-machines) | Class | Serves a state machine through its own typed operations |
