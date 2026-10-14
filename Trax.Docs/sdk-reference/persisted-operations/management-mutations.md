---
title: Management mutations and queries
description: "Reference for the operations.persistedOperations GraphQL fields that upload, deactivate, restore, list and show history of persisted operations."
type: reference
area: api
surface: graphql
package: Trax.Api.GraphQL.PersistedOperations
parent: Persisted Operations
grand_parent: SDK Reference
nav_order: 5
source:
  - Trax.Api/src/Trax.Api.GraphQL.PersistedOperations/GraphQL/PersistedOperationMutations.cs
  - Trax.Api/src/Trax.Api.GraphQL.PersistedOperations/GraphQL/PersistedOperationQueries.cs
  - Trax.Api/src/Trax.Api.GraphQL.PersistedOperations/GraphQL/MachineDocumentQueries.cs
---

# Management mutations and queries

[UsePersistedOperations](/docs/sdk-reference/persisted-operations/use-persisted-operations) registers six fields on the schema for browsing and editing persisted operations, all under the `operations.persistedOperations` namespace, resolved by `PersistedOperationMutations` and `PersistedOperationQueries`. Every field calls [IPersistedOperationsService](/docs/sdk-reference/persisted-operations/i-persisted-operations-service), the same service the Trax dashboard's persisted-operations pages call, so both accept and refuse the same things.

These fields always bypass persisted-operation enforcement (`PersistedOperationEnforcementMiddleware`, in the HotChocolate execution pipeline), since a client cannot send them by an id it has not uploaded yet. They are protected by the operations namespace's authorization posture: `GateOperations(...)`, the builder's `RequireAuthorization()`, or an explicit `AllowAnonymousOperations()`. A host that exposes the namespace with none of the three refuses to start.

## Mutations

### `uploadPersistedOperation`

Insert or update an operation. Runs schema validation, requires exactly one operation in the document, then runs the shape-diff guardrail.

| Input field | Type | Required | Notes |
|---|---|---|---|
| `id` | `String!` | yes | Build-time-stable identifier. Opaque string, no parse rule; the samples use `<name>_v<N>`. |
| `document` | `String!` | yes | The GraphQL document the id resolves to. |
| `description` | `String` | no | Operator-facing note recorded on the row. |
| `bypassShapeDiff` | `Boolean` | no | When true, allows an edit that changes the response shape. Default false. |
| `version` | `Int` | no | Operator-controlled metadata stored on the row. Not used for routing. Default `0`. |
| `tenantKey` | `String` | no | Tenant scope. Null targets the single-tenant row set. |

Payload: `{ success, operation, errors[] }`.

### `deactivatePersistedOperation`

Soft-delete; subsequent requests for the id resolve to null. The reason is required and recorded in the audit log. Deactivating an operation that is already deactivated succeeds, records the new reason, and sends the change to every node again.

| Input field | Type | Required |
|---|---|---|
| `id` | `String!` | yes |
| `reason` | `String!` | yes |
| `tenantKey` | `String` | no |

### `restorePersistedOperation`

Reactivate a deactivated row.

| Input field | Type | Required |
|---|---|---|
| `id` | `String!` | yes |
| `tenantKey` | `String` | no |

## Queries

### `persistedOperations(filter, take, skip)`

Paginated list, newest-updated first.

| Filter field | Type | Notes |
|---|---|---|
| `isActive` | `Boolean` | When set, restricts to active or deactivated rows. |
| `tenantKey` | `String` | Tenant scope. |
| `idStartsWith` | `String` | Prefix filter on the id. |

Defaults: `take` is 50 when not given, zero or negative, or over 200 (a `take` of 500 returns 50 rows, not 200); `skip` is floored at 0.

### `persistedOperation(id, tenantKey)`

Look up a single row. Returns null when missing.

### `persistedOperationHistory(id, tenantKey, take, skip)`

Audit history for an operation, most-recent first. Same `take` / `skip` defaults as `persistedOperations`.

### `machineDocuments`

`operations { machineDocuments { id machine operation irHash document } }`: the manifest of the
[machine documents](/docs/sdk-reference/graphql-api/machines#persisted-documents) this node serves, one per
operation of each exposed machine, with the IR hash of the machine it was generated for. A generated client reads
it at build time and sends each operation by its id. It sits directly under `operations`, behind the gate the host
declared for the namespace, and, like `persistedOperations`, it is admitted as an inline document when the host
requires persisted operations, since a client cannot ask for its ids by an id it does not have yet.

## Reserved ids

Ids starting with `trax_machine_` belong to the machine documents the host generates and registers itself. Upload,
deactivate and restore refuse them with the code `RESERVED_ID`, and `IPersistedOperationStore`'s `UpsertAsync`,
`DeactivateAsync` and `RestoreAsync` throw `PersistedOperationInputException`.

## Error payload

All mutations return errors via the payload `errors[]` array; mutations never throw to the client. Each entry has:

| Field | Type | Notes |
|---|---|---|
| `code` | `String!` | Stable code: `PARSE_FAILED`, `SCHEMA_VALIDATION_FAILED`, `SHAPE_DIFF_VIOLATION`, `NOT_FOUND`, `INVALID_INPUT` (an empty required field, or a document with other than one operation), `RESERVED_ID` (an id under `trax_machine_`, see [Reserved ids](#reserved-ids)), `CHANGE_NOT_BROADCAST` (the change is saved, but the broker did not confirm its broadcast). |
| `message` | `String!` | Human-readable message. On `SHAPE_DIFF_VIOLATION` it says to upload again with `bypassShapeDiff: true` if the change is shape-safe. |
| `locations` | `[Location!]` | 1-based line / column. Present on parse errors and most schema-validation errors. |
| `path` | `[String!]` | Response path. Present on some schema-validation errors. |
| `oldFingerprint` | `String` | Present only on `SHAPE_DIFF_VIOLATION`. |
| `newFingerprint` | `String` | Present only on `SHAPE_DIFF_VIOLATION`. |

`CHANGE_NOT_BROADCAST` is the one error that comes with an `operation`: the change was saved and is in force on the node that made it, so the payload carries the saved row, and `success` is false because the other nodes were not told. They pick the change up when their cached entry reaches its maximum age (`WithCacheMaxAge`, five minutes by default). Repeating the change sends it again.

See [PersistedOperationException](/docs/sdk-reference/persisted-operations/persisted-operation-exceptions) for the underlying exception types and how the codes map, and [Error Codes](/docs/sdk-reference/graphql-api/error-codes) for every code the endpoint returns.

## Example

```graphql
mutation Upload($input: UploadPersistedOperationInput!) {
  operations {
    persistedOperations {
      uploadPersistedOperation(input: $input) {
        success
        operation { id shapeFingerprint isActive }
        errors {
          code
          message
          locations { line column }
          oldFingerprint
          newFingerprint
        }
      }
    }
  }
}
```

```json
{
  "input": {
    "id": "userProfile_v1",
    "document": "query UserProfile($id: Int!) { user(id: $id) { id name email } }"
  }
}
```

## See also

- [UsePersistedOperations](/docs/sdk-reference/persisted-operations/use-persisted-operations)
- [IPersistedOperationsService](/docs/sdk-reference/persisted-operations/i-persisted-operations-service)
- [PersistedOperationException](/docs/sdk-reference/persisted-operations/persisted-operation-exceptions)
- [Mutations](/docs/sdk-reference/graphql-api/mutations)
