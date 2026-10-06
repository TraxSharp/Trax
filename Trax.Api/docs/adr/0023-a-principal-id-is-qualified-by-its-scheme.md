---
authors: [Theauxm]
areas: [auth]
status: accepted
---

# A principal id is qualified by its scheme

The `trax:principal-id` claim carries the id a resolver returned, qualified by the authentication
scheme that authenticated it: `{scheme}:{id}`. `sub = "abc"` from a JWT scheme named `Customer`
is `Customer:abc`; the API key `reporter` is `TraxApiKey:reporter`. Everything that reads the
claim sees the qualified id: `TryGetPrincipalId`, `TryGetTraxPrincipal`, the injected
`TraxPrincipal`, `TraxCaller`, the audit record, the mediator's per-principal limits, and the
owner-scope filters a host writes against any of them.

A resolver's id is only unique within its scheme. On a host with more than one issuer, two
issuers can both mint `sub = "abc"`, and an issuer that lets subjects be chosen (Trax's own
`Cognito.Issuer` mints whatever `Sub` it is given) can mint any `sub` at all. Unqualified, an
owner-scope filter keyed on the id treated issuer B's `abc` as issuer A's.

`TraxPrincipalExtensions.ToClaimsPrincipal` is the one place every Trax scheme builds its
principal, on HTTP and on sockets, so it qualifies the id there with `TraxPrincipalId.Qualify`.
A scheme name containing `:` is refused, because `A:B` with id `c` and `A` with id `B:c` would
read the same.

## Status

**Accepted.**

## Considered options

**A scheme-specific principal type, id left alone.** The `trax:principal-type` claim is optional
and every consumer that keys on the id would have to remember to key on both. The failure is
silent where it is forgotten; qualifying the id makes the safe key the only key.

**Qualify only on hosts with more than one scheme.** A host that adds a second issuer later
would change every id at that moment, with no release to announce it. Qualifying always makes
the shape one rule from the start.

**Qualify in each resolver.** A resolver does not know the scheme it runs under, and a host's
own resolver would have to remember to do it. Doing it where the claims are built covers every
resolver, Trax's and the host's.

**An opt-out.** Trax is fail-closed; a switch back to unqualified ids is a switch back to the
collision.

## Consequences

**Every stored principal id changes shape.** Rows keyed on the id (owner columns, audit
records, anything a host persisted from the claim) hold the unqualified form until migrated.
The migration is mechanical, `'{scheme}:' || owner_id` per scheme, and `TraxPrincipalId.Qualify`
computes the new value in code. The published docs carry the migration notes.

**A sign-in cookie issued before the upgrade keeps its unqualified id** until it is reissued,
since the cookie stores the claims it was issued with.

**A resolver still returns the local id.** `TraxPrincipal.Id` as a resolver builds it is the
scheme-local id; only a principal read back from the claims carries the qualified form. Project
a principal once: projecting a read-back principal qualifies it twice.

## Exemplars

- `PrincipalIdIsQualifiedBySchemeTests` pins it: the same `sub` from two JWT schemes yields two
  ids over HTTP and on a socket, an API-key principal has one qualified id on both transports,
  `ToClaimsPrincipal` and `TryGetTraxPrincipal` round-trip the qualified id, and a scheme name
  containing `:` is refused.

Not covered: nothing stops a host from building its own claims principal without
`ToClaimsPrincipal`, whose `trax:principal-id` is then whatever the host wrote.

## Changelog

- **2026-09-27**: Recorded.
