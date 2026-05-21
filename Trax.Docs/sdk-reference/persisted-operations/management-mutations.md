---
layout: default
title: Management mutations and queries
parent: Persisted Operations
grand_parent: SDK Reference
---

# Management mutations and queries

[UsePersistedOperations](/docs/sdk-reference/persisted-operations/use-persisted-operations) registers six fields on the schema for browsing and editing persisted operations, all under the `operations.persistedOperations` namespace. This matches the layout for every other Trax management feature (`operations.manifestGroups`, `operations.deadLetters`, etc.). The same fields back the Trax dashboard's persisted-operations pages and the sample client's manifest uploader.

These fields always bypass `PersistedOperationsMiddleware` enforcement. Persisting them by id would be a chicken-and-egg, and they are already protected by whatever ASP.NET auth middleware sits in front of the GraphQL endpoint.

## Mutations

### `uploadPersistedOperation`

Insert or update an operation. Runs schema validation, then the shape-diff guardrail.

| Input field | Type | Required | Notes |
|---|---|---|---|
| `id` | `String!` | yes | Build-time-stable identifier. Opaque string - no parse rule. The `<name>_v<N>` convention is recommended for readability. |
| `document` | `String!` | yes | The GraphQL document the id resolves to. |
| `description` | `String` | no | Operator-facing note recorded on the row. |
| `bypassShapeDiff` | `Boolean` | no | When true, allows an edit that changes the response shape. Default false. |
| `version` | `Int` | no | Operator-controlled metadata stored on the row. Not used for routing. Default `0`. |
| `tenantKey` | `String` | no | Tenant scope. Null targets the single-tenant row set. |

Payload: `{ success, operation, errors[] }`.

### `deactivatePersistedOperation`

Soft-delete; subsequent requests for the id resolve to null. The reason is required and recorded in the audit log.

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

Defaults: `take` capped to 200 (50 default), `skip` floored at 0.

### `persistedOperation(id, tenantKey)`

Look up a single row. Returns null when missing.

### `persistedOperationHistory(id, tenantKey, take, skip)`

Audit history for an operation, most-recent first. Same `take` / `skip` defaults as `persistedOperations`.

## Error payload

All mutations return errors via the payload `errors[]` array; mutations never throw to the client. Each entry has:

| Field | Type | Notes |
|---|---|---|
| `code` | `String!` | Stable code: `PARSE_FAILED`, `SCHEMA_VALIDATION_FAILED`, `SHAPE_DIFF_VIOLATION`, `NOT_FOUND`, `INVALID_INPUT`. |
| `message` | `String!` | Human-readable message. |
| `locations` | `[Location!]` | 1-based line / column. Present on parse errors and most schema-validation errors. |
| `path` | `[String!]` | Response path. Present on some schema-validation errors. |
| `oldFingerprint` | `String` | Present only on `SHAPE_DIFF_VIOLATION`. |
| `newFingerprint` | `String` | Present only on `SHAPE_DIFF_VIOLATION`. |

See [PersistedOperationException](/docs/sdk-reference/persisted-operations/persisted-operation-exceptions) for the underlying exception types and how the codes map.

## Example: upload

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
