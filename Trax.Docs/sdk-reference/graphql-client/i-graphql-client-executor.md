---
title: IGraphQLClientExecutor
description: "Reference for IGraphQLClientExecutor and GraphQLClientExecutor: how Run validates, sends and reads a request, and what it throws."
type: reference
area: api
parent: GraphQL Client
grand_parent: SDK Reference
nav_order: 10
package: Trax.Api.GraphQL.Client
namespace: Trax.Api.GraphQL.Client
source:
  - Trax.Api/src/Trax.Api.GraphQL.Client/Services/GraphQLClientExecutor/IGraphQLClientExecutor.cs
  - Trax.Api/src/Trax.Api.GraphQL.Client/Services/GraphQLClientExecutor/GraphQLClientExecutor.cs
---

# IGraphQLClientExecutor

Runs an [IGraphQLClientRequest](/docs/sdk-reference/graphql-client/i-graphql-client-request):
validates its query, sends it and reads the result. `GraphQLClientExecutor` is the implementation
[AddTraxGraphQLClient](/docs/sdk-reference/graphql-client/add-trax-graphql-client) registers as a
singleton (keyed, for [AddKeyedTraxGraphQLClient](/docs/sdk-reference/graphql-client/add-keyed-trax-graphql-client)).

## Signature

```csharp fragment
public interface IGraphQLClientExecutor
{
    Task<TReturn> Run<TReturn>(
        IGraphQLClientRequest<TReturn> request,
        CancellationToken cancellationToken = default);
}

public class GraphQLClientExecutor : IGraphQLClientExecutor
{
    public GraphQLClientExecutor(
        IGraphQLClientValidator validator,
        IGraphQLClientConfiguration configuration,
        ILogger<GraphQLClientExecutor>? logger = null);
}
```

## Returns

The result of the request's `Extract`.

## Behavior

`Run`:

1. Throws `ArgumentNullException` for a `null` request, and `OperationCanceledException` when the
   token is already cancelled.
2. Validates `Query` with the client's [validator](/docs/sdk-reference/graphql-client/i-graphql-client-validator).
   The first validation of a query text loads the schema when needed; later ones hit the cache.
3. Sends the request as a query or a mutation, according to the operation, through the
   configuration's `GraphQLHttpClient`.
4. Throws when the server returned errors or no data.
5. Checks the response shape when the client's strictness is not `Lenient` and the request's
   `UsesDefaultExtractor` is `true`. Drift is logged at Warning under
   `ILogger<GraphQLClientExecutor>` for `WarnOnDrift`.
6. Returns `request.Extract(data, options)`.

| Exception | When |
|-----------|------|
| `GraphQLValidationException` | The query does not validate, has no operation or more than one, or is a subscription against a schema without a subscription type (the default). Nothing is sent |
| `GraphQLSchemaIntrospectionException` | The schema could not be loaded |
| `GraphQLExecutionException` | The server returned GraphQL errors (`Errors`), the response had no `data`, or `Extract` produced `null` (`Errors` is empty) |
| `JsonException` | The default extractor could not read the data as `TReturn`. Not wrapped |
| `GraphQLResponseShapeException` | The response shape differs from `TReturn` under `ThrowOnDrift` |
| `NotSupportedException` | The operation is a subscription and the schema kept its subscription type (`RemoveSubscriptionsFromSchema = false`) |

Transport failures surface as the exceptions GraphQL.Client and `HttpClient` throw.

## Example

```csharp compile
public class PlayerLookup(IGraphQLClientExecutor graphql)
{
    public async Task<PlayerProfile?> Find(string id, CancellationToken ct)
    {
        try
        {
            return await graphql.Run(new GetPlayerRequest { Id = id }, ct);
        }
        catch (GraphQLExecutionException)
        {
            return null;
        }
    }
}
```

## See also

- [Client exceptions](/docs/sdk-reference/graphql-client/graphql-client-exceptions)
- [ResponseStrictness](/docs/sdk-reference/graphql-client/response-strictness)
- [IGraphQLClientConfiguration](/docs/sdk-reference/graphql-client/graphql-client-configuration)
