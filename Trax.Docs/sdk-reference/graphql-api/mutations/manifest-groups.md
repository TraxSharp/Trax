---
title: "operations.manifestGroups mutations"
description: "Reference for the operations.manifestGroups mutation namespace: updateManifestGroup, setManifestGroupsEnabled and setAllManifestGroupsEnabled."
type: reference
area: api
surface: graphql
package: Trax.Api.GraphQL
parent: Mutations
grand_parent: GraphQL API
nav_order: 7
source:
  - Trax.Api/src/Trax.Api.GraphQL/Mutations/ManifestGroupMutations.cs
---

# operations.manifestGroups mutations

`ManifestGroupMutations`, the type behind `mutation { operations { manifestGroups { ... } } }`.

The `operations.manifestGroups` namespace patches mutable fields on a manifest group and enables or disables groups in bulk. The dashboard calls the same underlying service, so a save from either surface produces an identical write.

## updateManifestGroup

Patches one or more fields on a manifest group. Fields left out of `input` are unchanged; `updatedAt` is bumped only when at least one field actually changed.

```graphql
mutation {
  operations {
    manifestGroups {
      updateManifestGroup(id: 7, input: {
        priority: 5
        isEnabled: false
      }) { success count message }
    }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | `Long!` | Yes | The manifest group's database ID |
| `input` | `UpdateManifestGroupInput!` | Yes | Patch payload (fields below) |

## UpdateManifestGroupInput fields

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `maxActiveJobs` | `Int` | `null` | New per-group concurrency limit, at least 1. `null` means "no change". To clear the limit, use `clearMaxActiveJobs` instead |
| `clearMaxActiveJobs` | `Boolean` | `false` | When `true`, sets `maxActiveJobs` to `null` (removes the per-group limit). Takes precedence if both this and `maxActiveJobs` are set |
| `priority` | `Int` | `null` | New priority, 0 to 31. `null` = no change |
| `isEnabled` | `Boolean` | `null` | Whether the group is active. `null` = no change |

**Returns**: `OperationResponse`. On success, `count` is the number of fields actually changed (zero if every supplied value already matched the persisted row). On failure (the group is not found, or a value is out of range), `success` is `false`, `message` explains, and no field is written.

## setManifestGroupsEnabled

Enables or disables the listed groups. Only groups whose flag differs are written, with `updatedAt` bumped.

```graphql
mutation {
  operations {
    manifestGroups {
      setManifestGroupsEnabled(ids: [1, 2], enabled: false) { success count message }
    }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `ids` | `[Long!]!` | Yes | 1 to 1000 manifest group ids |
| `enabled` | `Boolean!` | Yes | The flag to set |

**Returns**: `OperationResponse`. `count` is the number of groups changed, zero included. An empty list, or more than 1000 ids, returns `success: false` and changes nothing: an empty list never means "every group".

## setAllManifestGroupsEnabled

Enables or disables every manifest group. It is a field of its own so that "all" is something a caller asks for by name.

```graphql
mutation {
  operations {
    manifestGroups {
      setAllManifestGroupsEnabled(enabled: true) { success count message }
    }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `enabled` | `Boolean!` | Yes | The flag to set |

**Returns**: `OperationResponse`. `count` is the number of groups changed, zero included.

## See also

- [Mutations](/docs/sdk-reference/graphql-api/mutations)
- [operations.manifestGroups queries](/docs/sdk-reference/graphql-api/queries/manifest-groups)
