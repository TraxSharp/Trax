---
title: How the GraphQL client checks queries
description: "Why Trax.Api.GraphQL.Client validates hand-written queries against a schema instead of generating code, and how schema loading, caching and HTTP behave."
type: explanation
area: api
nav_order: 2
source:
  - Trax.Api/src/Trax.Api.GraphQL.Client/Services
---

# How the GraphQL client checks queries

`Trax.Api.GraphQL.Client` is the inverse of a code generator such as Strawberry Shake. Queries are
written by hand, as a string, an embedded `.graphql` file or a POCO the query is generated from,
and the client checks each one against the server's schema before it sends it. There are no
generated `.graphql.cs` files in the repository and no build step that needs the server, and the
check still catches a query the server would refuse. The steps are in
[Call another GraphQL server](/docs/api/call-another-graphql-server).

## Three ways to write a request, one executor

A raw string suits a query copied from a GraphQL playground, or one with fragments and unions. A
`.graphql` resource gives the query real syntax highlighting and leaves the C# class holding only
the variables. A [typed request](/docs/api/typed-requests) derives the query from the response
type, so renaming a property renames the field it selects. All three end up as query text run
through the same `IGraphQLClientExecutor`, so the choice is per request, not per application.

## Where the schema comes from

The check is only as good as the schema it checks against, and the three sources trade setup cost
for strength:

- **Introspection** (`IntrospectingSchemaProvider`, the default) asks the live endpoint. It needs no setup, but it is the weakest guarantee: the
  schema can change between the introspection and the request. A Trax server answers introspection
  only in Development unless its host admits the caller through `AllowIntrospection`, so against a
  production Trax server it is refused.
- **A file** (`FileSchemaProvider`, from `UseFileSchema`) is an SDL snapshot checked into the caller's repository. It works offline and in CI,
  and the check is against a known schema. The snapshot can fall behind the server, so keeping it
  fresh is a separate job: one that introspects the server and alerts on a difference, which keeps
  "the queries validate" and "the snapshot is current" two signals rather than one.
- **The assembly** (`AssemblySchemaProvider`, from `UseAssemblySchema` in `Trax.Api.GraphQL.Client.Trax`) builds the server's HotChocolate schema in
  process from the server's own configuration delegate. Nothing is fetched and nothing can drift,
  at the price of a binary dependency on the server's assembly.

All three accept the custom scalars a HotChocolate schema declares (`Any`, which every Trax schema
has, `UUID`, `DateTime`, `JSON` and the rest) as permissive scalars. The client checks that fields
and arguments exist; the server applies the scalar's own rules. All three drop the subscription
root by default, because the executor sends only queries and mutations, so a subscription is
refused when it is validated.

## Loading once, failing without hammering

A provider loads the schema on first use and shares it after that. A load that fails, because the
server was down or the file was missing, is not kept: one bad moment at boot does not leave the
client unable to validate until the process restarts. It is not retried on every request either.
The next load waits for a backoff that starts at up to a second and doubles with each consecutive
failure to at most 30 seconds, half of it random so processes that failed together do not retry
together, and a validation during the backoff fails at once with the last error. A server that is
down is therefore not introspected once per outbound request. A caller's cancellation token stops
that caller waiting; the load other callers wait on carries on.

A query that validated is remembered by its text, up to 1,024 distinct queries per client. Past
that, a query is still validated each time, just not remembered, which is why a request should
keep its query constant and pass values through variables.

## Checking the response as well as the query

Validation proves the server accepts the query. It does not prove the response type reads what the
query selects: a field added to the query and not to the POCO, or the reverse, deserializes without
complaint and loses data quietly. Response strictness compares the top level of each response with
the response type, cached per type and set of field names, and either logs or throws on a
difference. It is off by default, matching System.Text.Json, because a server adding a field should
not break callers in production; turning it on in development and integration tests catches the
drift the first time it happens. The modes are on
[ResponseStrictness](/docs/sdk-reference/graphql-client/response-strictness).

## The HTTP client

Each client sends through its own named `HttpClient` from `IHttpClientFactory`, so keyed clients
never share handlers. The client keeps that `HttpClient` for the life of the process, which means
the factory's handler rotation never reaches it. Following Microsoft's guidance for a long-lived
client, its primary handler is a `SocketsHttpHandler` whose pooled connections are replaced every
two minutes, so a DNS change is still picked up, and the factory's handler lifetime is infinite.

Nothing is retried by default. Every GraphQL request is a POST, and a mutation that reached the
server before the connection dropped is not safe to send again; a retry policy that cannot tell a
query from a mutation would double writes.

An `HttpClient` supplied with `ConfigureHttpClient` is not changed: requests go to the address the
client was registered with, set as GraphQL.Client's `EndPoint`, so one `HttpClient` can serve several
GraphQL clients. An `EndPoint` set to a different address is refused when the client is built.

## Keys name servers, not schemas

`AddKeyedTraxGraphQLClient` registers a client under a key that names the downstream server. The
library never inspects the key beyond comparing it by value, and it is unrelated to the server-side
`Namespace` that appears in a typed request's `Path`. Each key has its own configuration, schema and
validator cache, so a query written for one server fails validation when run through another's
client. The `[GraphQLClient(key)]` mark on a request only steers which client validates it at
startup; which server a request goes to is decided by the executor it is run through.

A startup validation that finds no request for its client refuses to start rather than passing
after checking nothing: that is a request someone forgot to mark. A request marked with a key no
client is registered under would be validated by nobody, so it refuses startup too.
