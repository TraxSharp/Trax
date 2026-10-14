---
title: GraphQL Client
description: "Index of the outbound GraphQL client reference: registration, the builder, request types, the executor, schema providers and startup validation."
type: landing
area: api
parent: SDK Reference
has_children: true
nav_order: 12
---

# GraphQL Client

The outbound client validates hand-written GraphQL queries against a server's schema and runs them. It
ships in three packages. For the concepts (query modes, schema sources, keyed clients), see
[GraphQL Client](/docs/api-graphql-client).

```csharp compile
services
    .AddTraxGraphQLClient(new Uri("https://api.example.com/graphql"))
    .UseFileSchema("schema.graphql")
    .WithStrictness(ResponseStrictness.ThrowOnDrift);
```

## Trax.Api.GraphQL.Client

The kernel. It has no dependency on trains, the mediator or a database.

- [AddTraxGraphQLClient](/docs/sdk-reference/graphql-client/add-trax-graphql-client): registers a client for one endpoint.
- [AddKeyedTraxGraphQLClient](/docs/sdk-reference/graphql-client/add-keyed-trax-graphql-client): registers a client under a DI key.
- [TraxGraphQLClientBuilder](/docs/sdk-reference/graphql-client/trax-graphql-client-builder): schema source, strictness, HTTP client and JSON options.
- [ValidateGraphQLClientAssembliesAsync](/docs/sdk-reference/graphql-client/validate-graphql-client-assemblies): validates a client's request types after the app is built.
- [IGraphQLClientRequest](/docs/sdk-reference/graphql-client/i-graphql-client-request): the request contract.
- [GraphQLResourceRequest](/docs/sdk-reference/graphql-client/graphql-resource-request): a request whose query is an embedded `.graphql` file.
- [GraphQLClientAttribute](/docs/sdk-reference/graphql-client/graphql-client-attribute): marks the keyed client a request belongs to.
- [IGraphQLClientExecutor](/docs/sdk-reference/graphql-client/i-graphql-client-executor): validates, sends and reads a request.
- [IGraphQLClientValidator](/docs/sdk-reference/graphql-client/i-graphql-client-validator): validates a query, or every request type in a set of assemblies.
- [ISchemaProvider](/docs/sdk-reference/graphql-client/i-schema-provider): where the schema comes from, and the built-in providers.
- [IGraphQLClientConfiguration](/docs/sdk-reference/graphql-client/graphql-client-configuration): the settings a client runs with, and the builder they come from.
- [ResponseStrictness](/docs/sdk-reference/graphql-client/response-strictness): how a response is checked against its response type.
- [Client exceptions](/docs/sdk-reference/graphql-client/graphql-client-exceptions): what `Run` and validation throw.

## Trax.Api.GraphQL.Client.Trax

- [UseAssemblySchema](/docs/sdk-reference/graphql-client/use-assembly-schema): validates against the server's HotChocolate schema built in-process.
- [UseStartupValidation](/docs/sdk-reference/graphql-client/use-startup-validation): validates a client's request types when the host starts.
- [TraxOutboundQueryAttribute](/docs/sdk-reference/graphql-client/trax-outbound-query-attribute): names the external endpoint a request calls, with `OutboundQueryDiscovery`, which lists them.

## Trax.Api.GraphQL.Client.Typed

- [TypedRequest](/docs/sdk-reference/graphql-client/typed-request): a request whose query is generated from its response type, and the attributes that shape it.

## How-to guides

- [Check outbound queries against a schema snapshot](/docs/api/check-outbound-queries-against-a-schema-snapshot)
