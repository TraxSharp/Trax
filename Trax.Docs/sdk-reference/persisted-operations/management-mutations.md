---
title: Management mutations and queries
description: "Reference for the persistedOperations GraphQL fields that upload, deactivate, restore and list persisted operations, and the gate in front of them."
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
  - Trax.Api/src/Trax.Api.GraphQL.PersistedOperations/Configuration/PersistedOperationsBuilder.Management.cs
---

# Management mutations and queries

[UsePersistedOperations](/docs/sdk-reference/persisted-operations/use-persisted-operations) registers six fields on the schema for browsing and editing persisted operations, all under a `persistedOperations` namespace at the root of the query and mutation types, resolved by `PersistedOperationQueries` and `PersistedOperationMutations`. Every field calls [IPersistedOperationsService](/docs/sdk-reference/persisted-operations/i-persisted-operations-service), the same service the Trax dashboard's persisted-operations pages call, so both accept and refuse the same things.

```graphql
query { persistedOperations { persistedOperations(take: 10) { totalCount } } }
mutation { persistedOperations { uploadPersistedOperation(input: { ... }) { success } } }
```

## The management gate

The fields are served only once the host declares who may use them, and each `persistedOperations` field carries that gate as `@authorize`:

```csharp compile
services.AddTraxGraphQL(graphql => graphql
    .UsePersistedOperations(po => po
        .SingleNode()
        .GateManagement(roles: "Deployer")
    )
);
```

| Call | Who gets in |
|---|---|
| `GateManagement(policy: "...")` | Authenticated callers the ASP.NET Core policy admits. The policy must be registered, or the host fails at startup. |
| `GateManagement(roles: "A,B")` | Callers holding at least one of the roles. |
| Repeated `GateManagement(...)` calls | Callers meeting every one of them. |
| `GateManagementToAuthenticatedUsers()` | Any authenticated caller. On a host with public sign-up that is anyone. |
| none | Nobody: the fields are not in the schema, and the dashboard shows no management pages. |

The Trax dashboard's persisted-operations pages check the same gate for the operator through `IPersistedOperationsManagementGate`, after the dashboard's own posture, so the Blazor dashboard, the React dashboard and GraphQL refuse the same people. Every gate requires an authenticated caller, whichever of its identities is authenticated, even when a policy alone would admit one that is not.

The gate is not the operations gate. `GateOperations` and `RequireAuthorization()` govern `operations`; `GateManagement` governs `persistedOperations`. A deploy pipeline that uploads documents holds the management role and reaches nothing under `operations`, and an operator who manages documents at the dashboard holds both. `RequireAuthorization()` on the builder still gates the whole endpoint, these fields included.

These fields always bypass persisted-operation enforcement (`PersistedOperationEnforcementMiddleware`, in the HotChocolate execution pipeline), since a client cannot send them by an id it has not uploaded yet. A document is admitted inline only when every operation in it selects nothing at the root but `persistedOperations`; mixing in another field sends it through enforcement like any other document. A host that declares no management gate serves none of these fields and gets no bypass.

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

## Reserved ids

Ids starting with `trax_machine_`, `trax_progress_` or `trax_client_` belong to the documents the host registers
itself ([generated documents](/docs/sdk-reference/persisted-operations/use-persisted-operations#generated-documents)),
and `trax_train_` to the train documents an earlier version generated. Upload,
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
