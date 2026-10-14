---
title: ISchemaProvider
description: "Reference for ISchemaProvider and its implementations: IntrospectingSchemaProvider, FileSchemaProvider and AssemblySchemaProvider."
type: reference
area: api
parent: GraphQL Client
grand_parent: SDK Reference
nav_order: 12
package: Trax.Api.GraphQL.Client
namespace: Trax.Api.GraphQL.Client
source:
  - Trax.Api/src/Trax.Api.GraphQL.Client/Services/SchemaProvider/ISchemaProvider.cs
  - Trax.Api/src/Trax.Api.GraphQL.Client/Services/SchemaProvider/IntrospectingSchemaProvider.cs
  - Trax.Api/src/Trax.Api.GraphQL.Client/Services/SchemaProvider/FileSchemaProvider.cs
---

# ISchemaProvider

Supplies the schema outbound queries are validated against. The builder's schema methods choose
the implementation: `UseIntrospection` (the default) registers `IntrospectingSchemaProvider`,
`UseFileSchema` registers `FileSchemaProvider`, and `UseAssemblySchema` registers
`AssemblySchemaProvider` from `Trax.Api.GraphQL.Client.Trax`.

## Signature

```csharp fragment
public interface ISchemaProvider
{
    Task<ISchema> GetSchemaAsync(CancellationToken cancellationToken = default);
}

public class IntrospectingSchemaProvider : ISchemaProvider
{
    public IntrospectingSchemaProvider(IGraphQLClientConfiguration configuration);
}

public class FileSchemaProvider : ISchemaProvider
{
    public FileSchemaProvider(string path);
    public FileSchemaProvider(string path, bool removeSubscriptionsFromSchema);
}
```

`ISchema` is graphql-dotnet's `GraphQL.Types.ISchema`.

## Members

`AssemblySchemaProvider` is in `Trax.Api.GraphQL.Client.Trax` (namespace `Trax.Api.GraphQL.Client.Trax`):

```csharp fragment
public sealed class AssemblySchemaProvider : ISchemaProvider
{
    public AssemblySchemaProvider(Action<IRequestExecutorBuilder> configure);
    public AssemblySchemaProvider(Action<IRequestExecutorBuilder> configure, bool removeSubscriptionsFromSchema);
}
```

| Provider | Loads the schema by |
|----------|---------------------|
| `IntrospectingSchemaProvider` | Sending the standard introspection query to the configuration's endpoint through its `GraphQLHttpClient`, then printing the result as SDL. Drops the subscription type when the configuration's `RemoveSubscriptionsFromSchema` is `true` |
| `FileSchemaProvider` | Reading the SDL file at `path`, absolute or relative to the working directory. The one-argument constructor drops the subscription type. Throws `ArgumentException` for a null, empty or whitespace path |
| `AssemblySchemaProvider` | Building a HotChocolate executor from `configure` in-process and printing its schema. The one-argument constructor drops the subscription type. Throws `ArgumentNullException` for a `null` delegate |

## Behavior

- Each built-in provider loads the schema on the first `GetSchemaAsync` call and shares it with every
  later call.
- A load that fails is not kept. The next load happens once a capped, jittered backoff has passed;
  a call before then gets the same failure.
- `cancellationToken` cancels the caller's wait, not a load other callers are waiting on.
- Every load failure is a `GraphQLSchemaIntrospectionException`: introspection failed or returned
  errors, the file is missing, empty or unreadable, or the SDL does not build into a schema.
- Custom scalars the schema declares are accepted as any value.

## Example

```csharp fragment
var provider = new FileSchemaProvider("schema.graphql");
var schema = await provider.GetSchemaAsync(ct);
Console.WriteLine(schema.Query?.Name);
```

## See also

- [TraxGraphQLClientBuilder](/docs/sdk-reference/graphql-client/trax-graphql-client-builder)
- [UseAssemblySchema](/docs/sdk-reference/graphql-client/use-assembly-schema)
- [IGraphQLClientValidator](/docs/sdk-reference/graphql-client/i-graphql-client-validator)
