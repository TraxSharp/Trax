---
title: IGraphQLClientValidator
description: "Reference for IGraphQLClientValidator, which validates an outbound query against the client's schema, and the ValidateAssembliesAsync extension."
type: reference
area: api
parent: GraphQL Client
grand_parent: SDK Reference
nav_order: 11
package: Trax.Api.GraphQL.Client
namespace: Trax.Api.GraphQL.Client
source:
  - Trax.Api/src/Trax.Api.GraphQL.Client/Services/GraphQLClientValidator/IGraphQLClientValidator.cs
  - Trax.Api/src/Trax.Api.GraphQL.Client/Extensions/GraphQLClientValidatorExtensions.cs
---

# IGraphQLClientValidator

Checks an outbound query against the client's schema before it is sent. Registered per client by
[AddTraxGraphQLClient](/docs/sdk-reference/graphql-client/add-trax-graphql-client); the executor and
assembly validation both call it. The static class `GraphQLClientValidatorExtensions` adds
`ValidateAssembliesAsync`.

## Signatures

```csharp fragment
public interface IGraphQLClientValidator
{
    Task<OperationType> ValidateAsync(string query, CancellationToken cancellationToken = default);
}

public static class GraphQLClientValidatorExtensions
{
    public static Task ValidateAssembliesAsync(
        this IGraphQLClientValidator validator,
        IEnumerable<Assembly> assemblies,
        CancellationToken cancellationToken = default);

    public static Task ValidateAssembliesAsync(
        this IGraphQLClientValidator validator,
        IEnumerable<Assembly> assemblies,
        Func<Type, bool>? typeFilter,
        CancellationToken cancellationToken = default);
}
```

`OperationType` here is `GraphQLParser.AST.OperationType`.

## Members

| Member | Description |
|--------|-------------|
| `ValidateAsync(query, cancellationToken)` | Validates `query` and returns its operation type (`Query`, `Mutation` or `Subscription`) |
| `ValidateAssembliesAsync(assemblies, cancellationToken)` | Validates every concrete, closed `IGenericGraphQLClientRequest` type in `assemblies`, whatever its `[GraphQLClient]` mark |
| `ValidateAssembliesAsync(assemblies, typeFilter, cancellationToken)` | The same, for the types `typeFilter` accepts (all of them when `null`) |

## Behavior

- `ValidateAsync` throws `ArgumentNullException` for a `null` query, and `GraphQLValidationException`
  when the document holds no operation or more than one, when the schema has no root type for the
  operation (a subscription, by default), or when schema validation fails.
- A query that passes is cached by its text, up to 1,024 queries per validator. Past that a query is
  still validated, but not remembered.
- The schema is loaded on the first validation through the client's
  [ISchemaProvider](/docs/sdk-reference/graphql-client/i-schema-provider); a load failure throws
  `GraphQLSchemaIntrospectionException`.
- `ValidateAssembliesAsync` creates each type without running a constructor, stops at the first
  failure, and does not fail when it finds no type. The ownership rule (unmarked types for the
  unkeyed client, marked types for their key) is applied by
  [ValidateGraphQLClientAssembliesAsync](/docs/sdk-reference/graphql-client/validate-graphql-client-assemblies),
  not here.

## Example

```csharp compile
public class QueryCheck(IGraphQLClientValidator validator)
{
    public Task CheckReportingQueries(CancellationToken ct) =>
        validator.ValidateAssembliesAsync(
            [typeof(QueryCheck).Assembly],
            type => type.Namespace?.EndsWith(".Reporting") == true,
            ct
        );
}
```

## See also

- [ValidateGraphQLClientAssembliesAsync](/docs/sdk-reference/graphql-client/validate-graphql-client-assemblies)
- [IGraphQLClientExecutor](/docs/sdk-reference/graphql-client/i-graphql-client-executor)
- [Client exceptions](/docs/sdk-reference/graphql-client/graphql-client-exceptions)
