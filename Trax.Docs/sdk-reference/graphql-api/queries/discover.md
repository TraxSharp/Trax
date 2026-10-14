---
title: "discover queries"
description: "Reference for the discover query namespace: the typed fields Trax generates for trains marked TraxQuery, their names, input and output."
type: reference
area: api
surface: graphql
package: Trax.Api.GraphQL
parent: Queries
grand_parent: GraphQL API
nav_order: 1
source:
  - Trax.Api/src/Trax.Api.GraphQL/Queries/DiscoverQueries.cs
  - Trax.Api/src/Trax.Api.GraphQL/TypeModules/TrainTypeModule.cs
---

# discover queries

The `discover` field on the root query type holds one field per train marked with [`[TraxQuery]`](/docs/sdk-reference/graphql-api/trax-graphql-attribute), resolved by the generated fields of `DiscoverQueries`. Entities marked `[TraxQueryModel]` add their fields here too (see [Query models](/docs/sdk-reference/graphql-api/query-models)). Only trains with the attribute appear under `discover`.

Each such train gets a single field named after the train (no prefix). The field accepts a strongly-typed `input` argument and returns the train's output type directly. Trains with `Namespace` set are grouped under a sub-namespace (e.g. `discover { players { lookupPlayer } }`).

## Field names

The field names are derived from the train's service interface name (or overridden via `[TraxQuery(Name = "...")]`):

1. Strip the `I` prefix
2. Strip the `Train` suffix
3. Use the result as the field name (lowercase first letter)

For example, `ILookupPlayerTrain` produces `lookupPlayer`.

## Example

Given a train annotated with `[TraxQuery]`:

```csharp compile
public record LookupPlayerInput
{
    public required string PlayerId { get; init; }
}

public record LookupPlayerOutput
{
    public required string PlayerId { get; init; }
    public required int Rank { get; init; }
}
```

The schema exposes:

```graphql
query {
  discover {
    lookupPlayer(input: { playerId: "player-42" }) {
      playerId
      rank
    }
  }
}
```

## Typed output

When a query train has a non-`Unit` output type, the output type is returned directly (not wrapped in a response type):

```graphql
type DiscoverQueries {
  lookupPlayer(input: LookupPlayerInput!): LookupPlayerOutput!
}
```

## `Unit` output

When a query train has `Unit` output, the field returns `RunTrainResponse` (`Trax.Api.DTOs`), the execution's metadata:

| Field | Type | Description |
|-------|------|-------------|
| `metadataId` | `Long!` | Metadata ID of the completed execution |

## See also

- [Queries](/docs/sdk-reference/graphql-api/queries)
- [dispatch mutations](/docs/sdk-reference/graphql-api/mutations/dispatch)
- [TraxQuery and TraxMutation](/docs/sdk-reference/graphql-api/trax-graphql-attribute)
