---
authors: [Theauxm]
areas: [graphql, platform]
status: accepted
---

# A request names the client that validates it

A host that calls two GraphQL servers registers a keyed client for each, and their request
types often live in one assembly. Startup validation scanned the whole assembly, so each
server's validator reached the other server's requests and refused to boot. A request type now
names its client with `[GraphQLClient(key)]`: a keyed client's validation checks only the types
marked with its key, the unkeyed client's only the unmarked types, and a validation that finds
no request for its client refuses to start.

## Status

**Accepted.**

## Considered options

**Reuse `[TraxOutboundQuery(Endpoint)]`.** It already names a server, but as free text for
tooling, documented as having no runtime effect, and it ships in the `.Trax` integration
package while the keyed helpers live in the kernel. Giving it a meaning now would quietly move
every request already carrying it out of the unkeyed client's validation, a check that stops
running without anyone noticing. A new attribute in the kernel, taking the same `object` key
the client was registered with, keeps both meanings honest.

**An overload of `UseStartupValidation` taking a type filter.** Works, but every host writes
its own predicate, and the request type still says nothing about where it belongs. The
attribute puts that fact on the request, where a reader looks for it.

**Validate everything and let each server's requests live in their own assembly.** What the
code did before. It forces a project layout to work around a validator.

## Consequences

**Finding nothing to validate is a failure.** A keyed validation with no request marked for its
key, or an unkeyed one with no unmarked request, is a request someone forgot to mark, so it
refuses to start rather than passing having checked nothing. The same holds for the
`ValidateGraphQLClientAssembliesAsync` helpers.

**A mark names a registered client.** A request marked with a key no client is registered
under is validated by none of them, so any validation of the assembly holding it refuses to
start, naming the request and the key. A mistyped key fails the boot instead of quietly leaving
its request unchecked.

**The mark governs validation only.** The executor a caller resolves still decides which
server a request goes to; running a request through the wrong key fails that server's schema
validation, as before. `IGraphQLClientValidator.ValidateAssembliesAsync` with an explicit
filter is unchanged and ignores the mark.

## Exemplars

- `KeyedStartupValidationTests` pins it: two keyed clients over two different schemas validate
  one shared assembly and both start; a key nothing is marked with refuses to start; the keyed
  helper validates only its own requests; a request belongs to the client its attribute names,
  matched by value.
- [GraphQL Client](/docs/api-graphql-client#talking-to-multiple-servers) is the rule this produces.
- `UnregisteredClientKeyTests` pins the registered-key rule: a request marked with a key no
  client has refuses startup for the unkeyed and for a keyed client, the validation helpers refuse
  it too, and a host registering every marked key starts.

## Changelog

- **2026-10-01**: A request marked with a key no client is registered under refuses startup; the
  gap this ADR recorded as not covered is closed.
- **2026-09-30**: Recorded.
