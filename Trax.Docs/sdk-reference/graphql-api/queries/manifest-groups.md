---
title: "operations.manifestGroups queries"
description: "Reference for the operations.manifestGroups query namespace: the paged group list, one group, group stats and the cross-group dependency graphs."
type: reference
area: api
surface: graphql
package: Trax.Api.GraphQL
parent: Queries
grand_parent: GraphQL API
nav_order: 10
source:
  - Trax.Api/src/Trax.Api.GraphQL/Queries/ManifestGroupQueries.cs
---

# operations.manifestGroups queries

`ManifestGroupQueries`, the type behind `operations { manifestGroups { ... } }`.

The `operations.manifestGroups` namespace exposes every read scoped to manifest groups: the paged list, single-group lookup, and the cross-group dependency graph the dashboard renders as a DAG. The list lives here (rather than as a sibling of `manifests` at the operations root) because both the namespace and a sibling `manifestGroups` field would camelCase to the same name in the schema, and HotChocolate would silently drop one.

## groups

Returns a paginated list of manifest groups, ordered by ID descending. Supports both offset-based and keyset cursor pagination.

```graphql
query {
  operations {
    manifestGroups {
      groups(skip: 0, take: 10) {
        items {
          id
          name
          maxActiveJobs
          priority
          isEnabled
          createdAt
          updatedAt
        }
        totalCount
        isEstimatedCount
        skip
        take
        nextCursor
      }
    }
  }
}
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `skip` | `Int!` | `0` | Number of records to skip (offset pagination). A negative value reads as `0` |
| `take` | `Int!` | `25` | Number of records to return, from 1 to 500. See [Page size](/docs/sdk-reference/graphql-api/queries/paged-result#page-size) |
| `nameContains` | `String` | `null` | Case-sensitive substring match on the group name |
| `afterId` | `Long` | `null` | Keyset cursor. Returns records with `id < afterId`. See [Pagination](/docs/sdk-reference/graphql-api/queries/paged-result#pagination) |

**Returns**: `PagedResult<ManifestGroupSummary>`

### ManifestGroupSummary fields

| Field | Type | Description |
|-------|------|-------------|
| `id` | `Long!` | Database ID |
| `name` | `String!` | Group name |
| `maxActiveJobs` | `Int` | Concurrency limit for the group (null = unlimited) |
| `priority` | `Int!` | Default priority for manifests in this group |
| `isEnabled` | `Boolean!` | Whether the group is active |
| `createdAt` | `DateTime!` | When the row was created |
| `updatedAt` | `DateTime!` | Last patch via `updateManifestGroup` |

## group

Single-group lookup by ID. Used by dashboards to pre-populate the group settings form before sending an `updateManifestGroup` patch.

```graphql
query {
  operations {
    manifestGroups {
      group(id: 7) {
        id
        name
        maxActiveJobs
        priority
        isEnabled
      }
    }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | `Long!` | Yes | Manifest group database ID |

**Returns**: `ManifestGroupSummary` (nullable; `null` when the group does not exist).

## stats

Execution roll-up for a set of manifest groups in a single round-trip: manifest count, executions by state, and last run per group. Backs the per-group stat columns on the dashboard's manifest groups list, which calls it with just the ids of the visible page, through the same `IOperationsService.GetManifestGroupExecutionStatsAsync` call. Every requested id gets a row (zeros when the group has no manifests or executions), returned in the order requested so the caller can zip it to its rows. The metadata side is served by `ix_metadata_manifest_state` and the manifest side by `ix_manifest_manifest_group_id`.

```graphql
query {
  operations {
    manifestGroups {
      stats(groupIds: [1, 2, 7]) {
        groupId
        manifestCount
        totalExecutions
        completed
        failed
        inProgress
        lastRun
      }
    }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `groupIds` | `[Long!]!` | Yes | The group ids to roll up, at most 1000 distinct ones. Duplicates are collapsed; an empty list returns an empty result. More than 1000 distinct ids fails the field with a GraphQL error coded `TRAX_TOO_MANY_IDS` |

**Returns**: `[ManifestGroupStats!]!`

### ManifestGroupStats fields

| Field | Type | Description |
|-------|------|-------------|
| `groupId` | `Long!` | The group these stats are for |
| `manifestCount` | `Long!` | Manifests belonging to the group |
| `totalExecutions` | `Long!` | Executions across all of the group's manifests |
| `completed` | `Long!` | Executions in `Completed` |
| `failed` | `Long!` | Executions in `Failed` |
| `inProgress` | `Long!` | Executions in `InProgress` |
| `lastRun` | `DateTime` | Start time of the group's most recent execution. Null when there are none |

## graph

Returns the 1-hop cross-group dependency neighborhood for a manifest group: every group containing a manifest the focal group's manifests depend on (upstream), every group containing a manifest depending on the focal group's manifests (downstream), and the focal group itself. Edges are directed parent → dependent.

```graphql
query {
  operations {
    manifestGroups {
      graph(groupId: 7) {
        nodes { id name isHighlighted }
        edges { fromId toId }
      }
    }
  }
}
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `groupId` | `Long!` | Yes | Database ID of the focal manifest group |

**Returns**: `ManifestGroupDependencyGraph` (nullable). Returns `null` only when the group does not exist. Empty groups still return a single-node graph (focal group, no edges) so the UI can render the focal node.

## dependencyGraph

The whole cross-group dependency graph in one shot: every manifest group as a node and every cross-group dependency (a manifest in one group depending on a manifest in another) as a directed parent → dependent edge. Nothing is highlighted. Backs the global DAG the dashboard renders on the manifest-groups page; `graph(groupId)` is the same shape narrowed to one group's neighborhood.

```graphql
query {
  operations {
    manifestGroups {
      dependencyGraph {
        nodes { id name isHighlighted }
        edges { fromId toId }
      }
    }
  }
}
```

**Returns**: `ManifestGroupDependencyGraph!` (never null; empty when there are no groups). Same-group dependencies are excluded, so a deployment whose groups never depend on each other returns nodes with no edges.

### ManifestGroupDependencyGraph fields

| Field | Type | Description |
|-------|------|-------------|
| `nodes` | `[DependencyGraphNode!]!` | All groups in the neighborhood plus the focal group |
| `edges` | `[DependencyGraphEdge!]!` | Cross-group edges only. Same-group dependencies are excluded |

### DependencyGraphNode fields

| Field | Type | Description |
|-------|------|-------------|
| `id` | `Long!` | Manifest group ID |
| `name` | `String!` | Group name |
| `isHighlighted` | `Boolean!` | `true` for the focal group; the UI uses this to render it differently |

### DependencyGraphEdge fields

| Field | Type | Description |
|-------|------|-------------|
| `fromId` | `Long!` | Parent group ID (the group whose manifests are depended on) |
| `toId` | `Long!` | Dependent group ID |

## See also

- [Queries](/docs/sdk-reference/graphql-api/queries)
- [operations.manifestGroups mutations](/docs/sdk-reference/graphql-api/mutations/manifest-groups)
