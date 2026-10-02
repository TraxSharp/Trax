---
layout: default
title: Qualified Principal Ids
description: Why the trax:principal-id claim is now qualified by authentication scheme, what reads it, and how to migrate ids already stored with the bare form.
parent: Reference
nav_order: 19
---

# Qualified Principal Ids

The `trax:principal-id` claim now carries the id a resolver returned **qualified by the
authentication scheme that authenticated it**: `{scheme}:{id}`.

| Caller | Before | Now |
|---|---|---|
| JWT `sub = "abc"` on the default scheme | `abc` | `TraxJwt:abc` |
| JWT `sub = "abc"` on `AddTraxJwtAuth("Customer", ...)` | `abc` | `Customer:abc` |
| API key account `reporter` | `reporter` | `TraxApiKey:reporter` |
| OIDC subject `u-1` | `u-1` | `TraxOidc:u-1` |
| Cognito user `e9fb...` on the default scheme | `e9fb...` | `TraxJwt:e9fb...` |

## Why

A resolver's id is unique only within its scheme. On a host with more than one issuer, two
issuers can both mint `sub = "abc"`, and an issuer that lets subjects be chosen can mint any
`sub` it likes. With the bare id, an owner-scope filter keyed on it treated one issuer's `abc`
as the other's. Qualifying the id by the scheme keeps them apart everywhere the id is a key.

## What reads the qualified id

Everything that reads the claim:

- `ClaimsPrincipal.TryGetPrincipalId(out var id)` and `FindFirst(TraxAuthClaimTypes.PrincipalId)`;
- `TryGetTraxPrincipal`, the injected [`TraxPrincipal`](/docs/sdk-reference/api-auth/injecting-trax-principal) and `TraxCaller.Principal`, whose `Id` is the qualified id;
- the audit record's [`PrincipalId`](/docs/sdk-reference/api-audit/trax-audit-entry);
- the mediator's per-principal concurrency buckets;
- any EF Core query filter or resolver you wrote against one of those.

What your resolver returns does not change. `TraxPrincipal.Id` as a resolver builds it is still
the scheme-local id (`sub`, the account name); `ToClaimsPrincipal(scheme)` qualifies it.

## Migrating stored data

Rows keyed on the principal id hold the old, unqualified form until you migrate them: owner
columns behind row-level filters, audit rows you query by principal, anything you persisted from
the claim. Rewrite each one with the prefix of the scheme that authenticated it. For a single
default JWT scheme:

```sql
UPDATE owned_books SET owner_id = 'TraxJwt:' || owner_id
WHERE owner_id NOT LIKE 'TraxJwt:%';
```

On a host with several schemes, a row's scheme is not in the row: migrate each owner according
to where its id came from, and treat a row you cannot attribute as unowned rather than guessing.

In code, `TraxPrincipalId.Qualify(scheme, id)` returns the id a principal will carry, for seeds
and fixtures:

```csharp
var ownerId = TraxPrincipalId.Qualify("Customer", "abc"); // "Customer:abc"
```

Key a filter on the qualified id alone. The scheme is inside it, so there is no second column to
remember.

## Other things to check

- **Sign-in cookies.** A cookie issued before the upgrade stores the claims it was issued with,
  so it keeps the unqualified id until it is reissued. If owner-scope data matters, end existing
  sessions at the upgrade (rotate the cookie name or the data-protection keys) so every
  principal carries one shape.
- **Scheme names.** A scheme name containing `:` is refused at authentication, because
  `A:B` + `c` and `A` + `B:c` would read the same. Rename such a scheme.
- **Projecting twice.** A principal read back with `TryGetTraxPrincipal` already carries the
  qualified id; passing it to `ToClaimsPrincipal` again qualifies it twice. Project the resolver's
  output once.
- **Rate limits and audit sinks** keyed on `trax:principal-id` see new keys; counters and
  dashboards bucketed on the old ids start fresh.
