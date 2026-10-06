---
authors: [Theauxm]
areas: [auth, data-model]
status: accepted
---

# Per-user data is scoped by a row filter that reads the principal, and exposed as a bare [TraxAuthorize]

Trax's authorization reads `[TraxAuthorize]` and `[TraxAllowAnonymous]`. It has no knowledge of the
EF `HasQueryFilter` that actually narrows a per-user entity to its owner's rows, so an entity that
is correctly gated but has no filter passes every Trax check and serves every user's rows to any
authenticated caller. `DataLayerGuards.OwnerScopeCompleteness` in Trax.Effect.Data.Testing is the
check that sees the filter. It is parameterized by the application's owner type and principal
accessor, because those are the application's; the mechanism is Trax's.

## Status

**Accepted.**

## Considered options

**Leave it to each application.** Where the check started, in one consumer's own test suite. It
was the only thing standing between that consumer and a cross-user leak, and every other consumer
exposing per-user entities has the same hole and no reason to know it.

**Count any query filter as ownership.** Simpler, and wrong. A soft-delete or visibility filter
(`deleted_at IS NULL`) mentions no user. Counting it would pull a shared entity into the census and
then fail it for a role gate that is correct for a row belonging to no one. Only a filter whose
expression references the principal accessor's type counts, which is detected structurally, so it
survives a renamed field or a rearranged predicate.

**Identify owner columns by name.** A name is a guess. Ownership comes from the model: a foreign
key to the owner type, or the owner type itself. Scalar owner ids with no modelled foreign key are
named explicitly by the consumer, and the default is none.

**Allow a role or policy gate on a per-user entity freely.** Refused. The row filter is the access
control; a gate on top can lock owners out of their own rows, and a policy can do it silently. A
gate is allowed only when the consumer lists the entity in `Gated` with a reason, and a gated
entity passes every other check as before: it still needs its principal-reading filter, and
`[TraxAllowAnonymous]` or a missing `[TraxAuthorize]` is still refused. The gate is an AND on top of
the filter, so it can only narrow who reads the rows.

**Let a gate excuse the filter, the way an exemption does.** Refused, because that is the one way a
gate could replace the filter: an admin-only role on an unfiltered entity serves every owner's rows
to every admin. An entity cannot be both gated and exempted, since the exemption would skip the
filter check the gate relies on.

## Consequences

An entity scoped only through a navigation (an answer whose poll holds the owner) has no owner key,
so its filter is the only thing marking it as per-user, and deleting the filter would drop it out
of the census rather than fail it. The consumer declares such entities with a reason, and the check
runs in both directions. Exemptions also need a reason, and one naming an entity the census would
not flag is reported, so a stale exemption cannot quietly cover whatever that entity becomes.

`Gated` entries go stale the same way, and are reported when the entity is not per-user, is not a
`[TraxQueryModel]`, or has a bare `[TraxAuthorize]`, and when an entry has no reason.

A second entity type mapped to the same table or view as a per-user entity reads the same rows, so
it is per-user whatever its own keys say, and needs the filter too. Without that rule an unfiltered
view mapping over a filtered table, gated or not, would be invisible to the census.

EF declares a filter on a hierarchy's root, so a derived type is judged by its root's filters.
Owned types share their owner's filter and are skipped. A many-to-many join entity with a foreign
key to the owner is reported like any other entity, and can be filtered through `UsingEntity`.

A filter present in the model can still be switched off where the entity is queried. The fixture
therefore also scans the consumer's source for `IgnoreQueryFilters()`, and for an EF10 named-filter
disable naming an owner-scope filter, on a per-user set. It is on whenever the census is, because a
scan that has to be switched on protects no one who did not know to; a call on a set the scan cannot
name is reported rather than assumed safe, and a file that switches the owner scope off on purpose
is allowlisted with a reason, checked in both directions like the other declarations.

## Exemplars

- `OwnerScopeCompletenessTests` covers each way an entity is recognised as per-user, the filter
  that does not count, a derived type, the navigation-scoped witness in both directions, each
  refused GraphQL posture, the exemption rules, the gated allowance (it never excuses the filter
  or an anonymous or undeclared posture, and stale entries fail), and a view mapping over a
  per-user table.
- `OwnerScopeFilterBypassTests` covers the source scan: `IgnoreQueryFilters()` on a per-user set
  (through a `DbSet` property, `Set<T>()`, or a derived type) fails, as does a named disable of the
  owner-scope filter, one whose names cannot be read, and a call on a set the scan cannot name; a
  call on shared rows, a named disable of another filter, and an allowlisted file pass, and a
  blank or stale allowlist entry fails.
- `DomainDataLayerGuardFixtureSelfTest` runs the census and the scan through the turnkey fixture the
  way a consumer would.

Not covered: the guard proves a principal-reading filter exists, not that it is correct. A filter
that reads the principal and compares the wrong column passes, and so does one with a bypass branch
(`principal.IsAdmin || e.OwnerId == principal.Id`): paired with an admin gate, that entity serves
every owner's rows to admins, by the filter's own design rather than the gate's. The census reads
the model; the source scan reads text, so it sees `IgnoreQueryFilters` at a call site but does not
follow a query built in one statement and filtered in another, and it cannot tell two contexts'
sets of the same name apart. An entity mapped with `ToSqlQuery` or to a function over per-user
tables, and a per-user entity reached through a navigation from an exposed type, are invisible to
both. Trax's own query paths do not call `IgnoreQueryFilters()`. Cross-user behavioural tests, one
caller trying to read another's rows, are what catch all of this, and they are the consumer's to
write.

## Changelog

- **2026-09-27**: The fixture also scans query code for `IgnoreQueryFilters` and named-filter
  disables on per-user sets, with a reasoned allowlist.
- **2026-09-27**: Recorded.
