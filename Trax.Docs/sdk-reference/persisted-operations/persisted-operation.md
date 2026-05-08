---
layout: default
title: PersistedOperation
parent: Persisted Operations
grand_parent: SDK Reference
---

# PersistedOperation

In-memory representation of a row in `trax.persisted_operation`. Returned by [IPersistedOperationStore](/docs/sdk-reference/persisted-operations/i-persisted-operation-store) reads.

## Properties

| Property | Type | Column | Notes |
|---|---|---|---|
| `TenantKey` | `string?` | `tenant_key` | Null at the C# boundary. Stored as `''` sentinel because the column participates in the composite primary key. |
| `Id` | `string` | `id` | Build-time-stable id, e.g. `userProfile_v1`. |
| `OperationName` | `string` | `operation_name` | Original GraphQL operation name (often differs from id by case). |
| `Version` | `int` | `version` | Numeric version parsed from the id suffix. |
| `Document` | `string` | `document` | Full GraphQL document text. |
| `ShapeFingerprint` | `string` | `shape_fingerprint` | sha-256 hex of the canonicalized response shape. |
| `IsActive` | `bool` | `is_active` | False indicates a soft-delete. Inactive rows do not serve requests. |
| `DeprecationReason` | `string?` | `deprecation_reason` | Required when `IsActive` is false. |
| `Description` | `string?` | `description` | Operator-facing note. |
| `CreatedAt` | `DateTime` | `created_at` | UTC. |
| `UpdatedAt` | `DateTime` | `updated_at` | UTC. |

## Schema

The table is created by Trax migration `035_persisted_operations.sql`, which also creates `trax.persisted_operation_history` for audit and rollback. Both tables live in the `trax` schema alongside the rest of the Trax tables (`metadata`, `manifest`, `log`, etc.).
