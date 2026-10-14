---
title: "OperationResponse"
description: "Reference for OperationResponse, the result type most operations mutations return: success, count, id and message."
type: reference
area: api
surface: graphql
package: Trax.Api.GraphQL
parent: Mutations
grand_parent: GraphQL API
nav_order: 10
source:
  - Trax.Api/src/Trax.Api/DTOs/OperationResponse.cs
---

# OperationResponse

The result type most fields under `mutation { operations { ... } }` return.

| Field | Type | Description |
|-------|------|-------------|
| `success` | `Boolean!` | Whether the operation succeeded |
| `count` | `Int` | Number of affected records, for a mutation that acts on a set or patches fields; `null` otherwise |
| `id` | `Long` | The one row the operation acted on, when there is one: the work queue entry `queueTrain`, `requeueExecution` and `resumeExecution` created, the execution `runTrain` started, the group `updateManifestGroup` patched. `null` otherwise |
| `message` | `String` | Human-readable status message |

## See also

- [Mutations](/docs/sdk-reference/graphql-api/mutations)
