---
authors: [Theauxm]
repos: [mediator, api]
areas: [platform, graphql]
status: accepted
---

# Train roles match exactly, like @authorize

A train's `[TraxAuthorize(Roles = ...)]` roles are compared with the caller's role claims
ordinally: exact and case-sensitive, the way `@authorize` on a query model compares them through
`IsInRole`. Train discovery keeps the roles as declared, and `TrainAuthorizationService` compares
them unchanged.

## Status

**Accepted.** Trax.Mediator carries its half (discovery keeps the declared roles). Trax.Api
still upper-cases both sides until its half ships, which it can only do after the Mediator
release, so until then matching stays case-insensitive.

## Considered options

**Case-insensitive, with ordinal case folding (`OrdinalIgnoreCase`).** Closes the lookalike
problem below and keeps `admin` matching `Admin`. Rejected because it keeps the two
authorization surfaces disagreeing: a role that satisfies a train would still be refused by
`@authorize` on the query model, and the reverse, for the same principal.

**Keep upper-casing with the invariant culture.** What both sides did. Culture case mapping is
not a comparison: it maps the long s (`ſ`) to `S`, so a claim of `SUPERUSER` satisfied a role
declared as `ſuperuser`, and a role check that equates characters a person reads as different is
the wrong default for a security check.

## Consequences

A host whose role claims differ in case from its declared roles (`Roles = "admin"` with a claim of
`Admin`) is refused once the Api half ships. Declare roles in the casing the identity provider
issues them.

`TrainRegistration.RequiredRoles`, and the roles the `trains` query reports, are now the declared
casing rather than upper-cased.

## Exemplars

**Enforced elsewhere:** `TrainDiscoveryAuthorizationTests` in Trax.Mediator (roles kept as
declared, and lookalikes kept distinct).

Not covered: the Api's comparison, until its half ships with a test that a role differing only
in case, or only under case mapping, is refused.

## Changelog

- **2026-09-27**: Recorded, with the Mediator half.
