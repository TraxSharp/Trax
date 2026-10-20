---
title: Check outbound queries against a schema snapshot
description: "Validate every outbound GraphQL query against a checked-in SDL file, in a test and at startup, with no server running."
type: how-to
area: api
nav_order: 30
---

# Check outbound queries against a schema snapshot

The GraphQL client validates each query against the server's schema before it sends it. With a
checked-in SDL file as the schema, the same check runs in CI and at startup without reaching the
server, which matters against a Trax server outside Development: it refuses introspection unless its
host admits the client.

## 1. Check in the schema

Save the server's schema as SDL in the client's repository, for example `Schemas/players.graphql`,
and copy it to the output directory:

```xml
<ItemGroup>
  <None Include="Schemas/*.graphql" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

## 2. Point the client at it

```csharp compile
using Trax.Api.GraphQL.Client;
using Trax.Api.GraphQL.Client.Trax;

builder.Services
    .AddTraxGraphQLClient(new Uri("https://players.example.com/graphql"))
    .UseFileSchema(Path.Combine(AppContext.BaseDirectory, "Schemas", "players.graphql"))
    .UseStartupValidation(typeof(Program).Assembly);
```

[UseStartupValidation](/docs/sdk-reference/graphql-client/use-startup-validation) stops the host
from starting when a query no longer validates. Without the `Trax.Api.GraphQL.Client.Trax` package,
call [ValidateGraphQLClientAssembliesAsync](/docs/sdk-reference/graphql-client/validate-graphql-client-assemblies)
after `builder.Build()` instead.

## 3. Check it in a test

Build a container with the same registration and validate the assembly that holds the request
types. It needs no server.

```csharp compile
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Client;

public static class OutboundQueryChecks
{
    public static async Task Every_query_validates_against_the_snapshot()
    {
        var services = new ServiceCollection();
        services
            .AddTraxGraphQLClient(new Uri("https://players.example.com/graphql"))
            .UseFileSchema(Path.Combine(AppContext.BaseDirectory, "Schemas", "players.graphql"));

        await using var provider = services.BuildServiceProvider();
        await provider.ValidateGraphQLClientAssembliesAsync(typeof(OutboundQueryChecks).Assembly);
    }
}
```

The call throws `GraphQLValidationException` naming the failing query, or
`InvalidOperationException` when no request type belongs to the client. A request for a keyed client
is checked with the keyed overload, `ValidateGraphQLClientAssembliesAsync(key, assemblies)`.

## 4. Keep the snapshot current

The snapshot only proves the queries match the file. Refresh it whenever the server's schema
changes, for example in a scheduled job that fetches the schema from a non-production server and
fails on a difference, so "the queries validate" and "the snapshot is current" are two separate
signals.

## See also

- [GraphQL Client](/docs/api/call-another-graphql-server)

## SDK Reference

> [AddTraxGraphQLClient](/docs/sdk-reference/graphql-client/add-trax-graphql-client) | [TraxGraphQLClientBuilder](/docs/sdk-reference/graphql-client/trax-graphql-client-builder) | [UseStartupValidation](/docs/sdk-reference/graphql-client/use-startup-validation) | [ValidateGraphQLClientAssembliesAsync](/docs/sdk-reference/graphql-client/validate-graphql-client-assemblies) | [ISchemaProvider](/docs/sdk-reference/graphql-client/i-schema-provider)
