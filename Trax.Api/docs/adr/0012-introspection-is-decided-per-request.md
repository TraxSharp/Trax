---
authors: [Theauxm]
areas: [graphql, auth]
status: accepted
---

# Introspection is decided per request

Whether a caller may read the Trax schema is answered once per request, with that request's
`HttpContext`, and the same answer governs all three ways of reading it: introspection fields in
an operation, the schema download (`?sdl`, `/schema`, `/schema.graphql`) and the GraphQL IDE. The
default is to allow in Development and refuse elsewhere; a predicate given to
`AllowIntrospection` replaces that default in every environment.

## Status

**Accepted.**

## How it is wired

HotChocolate reads its `DisableIntrospection` option once per executor, when no request exists,
and its server options for the schema download and the IDE are fixed when the endpoint is
built. Neither can hold a per-request answer, so:

- The schema-level switch is always on (`DisableIntrospection(true)`). HotChocolate's
  introspection rule then refuses any request that does not carry an
  `IntrospectionRequestOverrides` allowance.
- A request middleware placed immediately before document validation grants that allowance
  when the policy says so. It is part of the execution pipeline, so HTTP POST, GET, multipart
  and WebSocket all pass through it, and nothing depends on a transport's interceptor
  remembering to ask.
- `UseTraxGraphQL` wraps the endpoint's request delegate so a schema download or an IDE request
  is answered 404 when the policy refuses, and an allowed schema download is marked
  `Cache-Control: private`.
- With no predicate and outside Development the answer is always no, so the schema's own
  server options also switch the download and the IDE off. That covers a host that maps the
  endpoint itself with `MapGraphQL`.

## Considered options

**Allow in-process requests.** A request with no `HttpContext` is built by host code, and
treating it as trusted would keep in-process schema tests working without setup. Rejected
because the only signal is the absence of a feature: a transport that forgot to attach the
`HttpContext` would read as trusted. A request without one is answered by the environment
alone, so it is allowed in Development and refused elsewhere, and the predicate is never
called with a request it cannot see.

**Tie the IDE and schema download to Development only.** Simpler, and it would leave a host that
opens introspection to its administrators with an IDE that cannot be served to them. Following
the same predicate keeps one answer for one question.

**Grant the allowance from the HTTP and socket interceptors.** It works, and it makes every
interceptor, including a host's own, responsible for a security decision. The request
middleware makes the decision once, below all of them.

## Consequences

**A test that reads the schema in-process needs a Development environment** or runs over HTTP.
The Trax test suite registers one where it does.

**A predicate decides in Development too.** A host that sets `AllowIntrospection(_ => false)`
gets no introspection anywhere, which is what the call says.

**An allowance the host grants itself is kept.** A host interceptor that calls
`AllowIntrospection()` on the request builder is not overridden; the middleware only ever
adds an allowance.

## Exemplars

- `IntrospectionPerRequestTests` runs every case over a real `TestServer`: POST, GET and
  WebSocket in Production, Staging and Development, a predicate in each direction, the
  schema download paths, the IDE, and a self-mapped endpoint.
- [API Security](/docs/api-security#graphql-hardening-defaults) is the rule this produces.

Not covered: the per-request gate on the download and the IDE applies to the endpoint
`UseTraxGraphQL` maps. A self-mapped endpoint with a predicate set keeps HotChocolate's
defaults for both.

## Changelog

- **2026-09-27**: Recorded.
