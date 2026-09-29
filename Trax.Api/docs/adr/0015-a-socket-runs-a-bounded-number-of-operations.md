---
authors: [Theauxm]
areas: [graphql]
status: accepted
---

# A socket runs a bounded number of operations

One WebSocket connection runs at most `MaxOperationsPerConnection` operations at once, 100 unless
the GraphQL builder sets another positive number. An operation the connection starts past the
limit gets a GraphQL error with code `TRAX_SOCKET_OPERATION_LIMIT` and takes no place; the
connection stays open. A place frees when one of the connection's operations completes.

`TraxCompositeSocketInterceptor` counts: it keeps each connection's running operation ids,
admits an operation in `OnRequestAsync` or marks its request, and releases it in
`OnCompleteAsync`. A request middleware placed before HotChocolate's document cache turns the
mark into the error.

## Status

**Accepted.**

## Considered options

**No limit.** Every subscription on a socket is a stream the server keeps and a copy of every
event it sends, so without a bound one connection decides how much work each publish costs.

**Refusing inside the interceptor.** The natural place, and it does not work: HotChocolate masks
an exception thrown from `OnRequestAsync`, so the client would see a generic error and the
operation's place would already be counted. Marking the request and refusing in the pipeline
gives the client a coded error, and the refused operation never held a place.

**Closing the connection past the limit.** It takes every running operation with it, including
the ones that were within the limit, where refusing the one extra operation costs the client
nothing it had.

**Counting subscriptions only.** Queries and mutations sent over the socket also hold server
work until they complete, and counting every operation keeps the rule one sentence.

## Consequences

**A client running more than 100 operations on one socket now gets errors.** It raises the limit
with `MaxOperationsPerConnection(n)` or spreads the operations over more connections.

**The limit is per connection.** It does not bound how many connections a client opens.

**A host that replaces the socket interceptor through `ConfigureSchema` loses the limit** with
the rest of the composite, as ADR 0006 describes for authentication.

## Exemplars

- `SocketOperationLimitTests` pins it: past the limit an operation is refused; a completed
  operation frees its place; the limit is per connection; a refused operation takes no place;
  the default is 100 on the builder and on the interceptor; a non-positive limit is rejected;
  and over a real socket with a limit of 2 the third subscription gets
  `TRAX_SOCKET_OPERATION_LIMIT`.

## Changelog

- **2026-09-27**: Recorded.
