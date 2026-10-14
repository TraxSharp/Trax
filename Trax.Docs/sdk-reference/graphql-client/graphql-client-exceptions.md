---
title: Client exceptions
description: "Reference for the exceptions the Trax GraphQL client throws: execution, validation, response shape and schema introspection failures."
type: reference
area: api
parent: GraphQL Client
grand_parent: SDK Reference
nav_order: 16
package: Trax.Api.GraphQL.Client
namespace: Trax.Api.GraphQL.Client
source:
  - Trax.Api/src/Trax.Api.GraphQL.Client/Exceptions/GraphQLValidationException.cs
  - Trax.Api/src/Trax.Api.GraphQL.Client/Exceptions/GraphQLExecutionException.cs
  - Trax.Api/src/Trax.Api.GraphQL.Client/Exceptions/GraphQLSchemaIntrospectionException.cs
  - Trax.Api/src/Trax.Api.GraphQL.Client/Exceptions/GraphQLResponseShapeException.cs
---

# Client exceptions

The exceptions the GraphQL client throws from validation and from
[IGraphQLClientExecutor.Run](/docs/sdk-reference/graphql-client/i-graphql-client-executor). All
derive from `Exception`.

## Signature

```csharp fragment
public class GraphQLValidationException : Exception
{
    public GraphQLValidationException(string query, IReadOnlyList<ExecutionError> errors);
    public GraphQLValidationException(string query, IReadOnlyList<ExecutionError> errors, string detail);
    public string Query { get; }
    public IReadOnlyList<ExecutionError> Errors { get; }
}

public class GraphQLExecutionException : Exception
{
    public GraphQLExecutionException(IReadOnlyList<GraphQLError> errors);
    public GraphQLExecutionException(string message, Exception inner);
    public IReadOnlyList<GraphQLError> Errors { get; }
}

public class GraphQLSchemaIntrospectionException : Exception
{
    public GraphQLSchemaIntrospectionException(string message);
    public GraphQLSchemaIntrospectionException(string message, Exception inner);
}

public class GraphQLResponseShapeException : Exception
{
    public GraphQLResponseShapeException(
        Type targetType,
        IReadOnlyList<string> extraJsonFields,
        IReadOnlyList<string> missingJsonFields);
    public Type TargetType { get; }
    public IReadOnlyList<string> ExtraJsonFields { get; }
    public IReadOnlyList<string> MissingJsonFields { get; }
}
```

## GraphQLValidationException

Thrown before anything is sent, when a query has no operation, more than one, an operation type the
schema has no root for, or fails schema validation. `Query` is the query text. `Errors` holds
graphql-dotnet's `ExecutionError`s, empty when the failure was not a schema-validation error. The
message is `GraphQL query failed schema validation: <errors>. Query: <query>`, or the given
`detail` in place of the head.

## GraphQLExecutionException

Thrown by `Run` when the server answered with GraphQL errors (in `Errors`, GraphQL.Client's
`GraphQLError`s, joined into the message), when the response carried no `data`, or when the data
could not be extracted. `Errors` is empty for the last two.

## GraphQLSchemaIntrospectionException

Thrown when the schema cannot be loaded: introspection failed or returned errors, an SDL file is
missing, empty or unreadable, or the SDL does not build into a schema. The provider tries again
after a backoff; see [ISchemaProvider](/docs/sdk-reference/graphql-client/i-schema-provider#behavior).

## GraphQLResponseShapeException

Thrown by `Run` under `ResponseStrictness.ThrowOnDrift` when the response's fields and the response
type's properties differ. `TargetType` is the response type, `ExtraJsonFields` the fields it does not
declare, `MissingJsonFields` the properties the response did not include. The message names the type
and both lists:

```
Response shape does not match PlayerProfile: fields declared on POCO not in response: level.
```

## Example

```csharp compile
public class SafeLookup(IGraphQLClientExecutor graphql, ILogger<SafeLookup> logger)
{
    public async Task<PlayerProfile?> Find(string id, CancellationToken ct)
    {
        try
        {
            return await graphql.Run(new GetPlayerRequest { Id = id }, ct);
        }
        catch (GraphQLValidationException ex)
        {
            logger.LogError(ex, "Query rejected by the schema: {Query}", ex.Query);
            throw;
        }
        catch (GraphQLResponseShapeException ex)
        {
            logger.LogWarning("Missing fields: {Fields}", string.Join(", ", ex.MissingJsonFields));
            return null;
        }
    }
}
```

## See also

- [IGraphQLClientExecutor](/docs/sdk-reference/graphql-client/i-graphql-client-executor)
- [ResponseStrictness](/docs/sdk-reference/graphql-client/response-strictness)
- [ValidateGraphQLClientAssembliesAsync](/docs/sdk-reference/graphql-client/validate-graphql-client-assemblies)
