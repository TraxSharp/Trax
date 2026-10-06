---
authors: [Theauxm]
areas: [graphql, auth]
status: accepted
---

# GraphQL GET is off unless the host opts in

HotChocolate executes queries sent over HTTP GET by default. A browser attaches a `SameSite=Lax`
cookie to a cross-site top-level navigation, so with GET on, a link on another site could run a
`[TraxQuery]` train as the signed-in user: the response is not readable cross-site, but the train
runs, with its side effects and its metadata rows. Trax therefore serves GraphQL over POST only.
A host that needs GET calls `AllowGetRequests()` on the GraphQL builder, and even then a GET must
carry the `GraphQL-preflight` header and may run only queries.

## Status

**Accepted.**

## Considered options

**Keep GET on and require the preflight header.** Closes the cross-site case on its own, since a
navigation cannot add a header. Rejected as the default because nothing in Trax needs GET: its
own client, the dashboard and the samples all POST. A transport nobody uses is surface to defend,
so it starts off, and the header stays as a second guard for hosts that turn it back on.

**Off with no way back.** Too strict: a CDN caching persisted queries by id is a legitimate use of
GET. The opt-in is one call, visible in the diff.

**Leave it to the host's `ModifyServerOptions`.** What happened before this decision, and it
meant the default was HotChocolate's, not Trax's. Trax sets the options on its schema; a host
that calls `ModifyServerOptions` on the `trax` schema afterwards still overrides them, which is a
deliberate act rather than a forgotten one.

## Consequences

**A client that sent GraphQL queries over GET stops working** until the host calls
`AllowGetRequests()` and the client sends `GraphQL-preflight: 1`. That includes persisted
operations requested by id over GET.

**The IDE page and the SDL download are not affected.** They are separate HotChocolate options
(`Tool`, `EnableSchemaRequests`), still served over GET, and introspection keeps its own gate.

**The setting lives on the schema**, so it holds whether the endpoint is mapped with
`UseTraxGraphQL` or directly with `MapGraphQL(path, "trax")`.

## Exemplars

- `GraphQLGetRequestsTests` pins it: a GET query is refused by default and a POST served; with
  the opt-in a GET query with the preflight header is served, one without it is refused, and a
  GET mutation is refused; the IDE page is still served with GET off.

Not covered: nothing stops a host from turning the options back on through `ModifyServerOptions`
on the `trax` schema. That is an explicit override, and this decision does not try to prevent it.

## Changelog

- **2026-09-27**: Recorded.
