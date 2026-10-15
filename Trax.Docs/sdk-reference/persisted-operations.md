---
title: Persisted Operations
description: "Index of the Trax.Api.GraphQL.PersistedOperations reference: registration, builders, store, service, caching, broadcasting and management fields."
type: landing
area: api
parent: SDK Reference
has_children: true
nav_order: 11
---

# Persisted Operations

Reference for `Trax.Api.GraphQL.PersistedOperations`. For what the feature does and how to roll it
out, see [Persisted Operations](/docs/persisted-operations).

## Registration

- [UsePersistedOperations](/docs/sdk-reference/persisted-operations/use-persisted-operations): wires storage, enforcement, caching and invalidation on a GraphQL host.
- [PersistedOperationsBuilder](/docs/sdk-reference/persisted-operations/persisted-operations-builder): the options `UsePersistedOperations` takes.
- [PersistedOperationsOptions](/docs/sdk-reference/persisted-operations/persisted-operations-options): the resolved options.
- [AddPersistedOperationStore](/docs/sdk-reference/persisted-operations/add-persisted-operation-store): registers the store on a host with no GraphQL server.
- [UsePersistedOperationsEnforcement](/docs/sdk-reference/persisted-operations/use-persisted-operations-enforcement): kept for compatibility; where enforcement runs.

## Storage and services

- [IPersistedOperationsService](/docs/sdk-reference/persisted-operations/i-persisted-operations-service): the management surface the GraphQL fields and the dashboard share.
- [IPersistedOperationStore](/docs/sdk-reference/persisted-operations/i-persisted-operation-store): programmatic reads and writes.
- [IPersistedOperationValidator](/docs/sdk-reference/persisted-operations/i-persisted-operation-validator): validates a document before it is stored.
- [IPersistedOperationCache](/docs/sdk-reference/persisted-operations/i-persisted-operation-cache): the optional lookup cache.
- [IPersistedOperationBroadcaster](/docs/sdk-reference/persisted-operations/i-persisted-operation-broadcaster): tells other nodes about a change.
- [PersistedOperation](/docs/sdk-reference/persisted-operations/persisted-operation): the stored row and its history.
- [ShapeFingerprintComputer](/docs/sdk-reference/persisted-operations/shape-fingerprint-computer): the response-shape hash.
- [PersistedOperationException](/docs/sdk-reference/persisted-operations/persisted-operation-exceptions): the structured failures and their codes.

## GraphQL

- [Management mutations and queries](/docs/sdk-reference/persisted-operations/management-mutations): the root `persistedOperations` fields, and the management gate in front of them.
