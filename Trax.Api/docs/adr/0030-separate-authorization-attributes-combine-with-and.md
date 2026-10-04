---
authors: [Theauxm]
areas: [graphql, auth]
status: accepted
---

# Separate authorization attributes combine with AND

A surface can carry more than one `[TraxAuthorize]`: on a class and on a method, on a train's
interface and on its class, twice on one entity. The operations namespace can take more than one
`GateOperations(...)` call. Each attribute, and each call, is a requirement of its own, and the
caller must meet every one. Within one attribute a comma-separated `Roles` list is any of; across
attributes, role lists are all of, as policies always were. This is how ASP.NET Core combines
separate `[Authorize]` attributes, and it means a narrower attribute can only narrow: a method's
`[TraxAuthorize(Roles = "Support")]` under a class-level `[TraxAuthorize(Roles = "Admin")]`
requires both roles, never either. A role list that names no role (`","`) is refused at startup
rather than read as no requirement.

## Status

**Accepted.**

## Considered options

**Union the roles across attributes (what Trax did).** Every role from every attribute went into
one list and the caller needed any one of them. It reads naturally for two attributes on one
class, and it is wrong the moment the attributes sit at different scopes: the method-level
attribute someone adds to restrict one field to `Support` lets every `Support` caller past the
class-level `Admin` gate. Adding an attribute widened access, the opposite of what anyone adding
one means. A host also had no way to require two roles at once.

**Keep the union, and refuse a member-level attribute naming roles outside the class-level
set.** Closes the widening at one scope pair, leaves the train interface/class pair and two
`GateOperations` calls with the same surprise, and keeps a rule no other .NET framework has. A
reader who knows ASP.NET Core would still guess wrong.

**AND, as ASP.NET Core does.** `AuthorizationPolicy.CombineAsync` turns each `[Authorize]` into
its own requirements and every requirement must succeed. HotChocolate already evaluates every
`@authorize` on a field and requires all of them, so emitting one directive per attribute's role
list is the whole implementation on the GraphQL side. Chosen.

## Consequences

**A behaviour change for any surface with two role-bearing attributes.** A host that wrote
`[TraxAuthorize(Roles = "A")] [TraxAuthorize(Roles = "B")]` to mean "A or B" now requires both,
and gets the old meaning back with one attribute, `[TraxAuthorize(Roles = "A,B")]`. The change
fails closed: a caller who passed before and should not have is refused, never the reverse.

**Trains read their role lists from the attributes.** `TrainRegistration.RequiredRoles`, built by
Trax.Mediator, is the union and cannot tell one list from two. `TrainRoleRequirements` reads the
lists again from the same carriers discovery reads (the implementation, its base chain and its
interfaces). A registration whose attributes do not account for its `RequiredRoles`, one built by
hand, is taken as a single list.

**`GateOperations(roles: ",")` refuses startup**, as `[TraxAuthorize(Roles = ",")]` on an entity
already did, and so does the same attribute, or an empty `Policy`, on a resolver. Each would
otherwise have dropped to "any authenticated caller".

## Exemplars

- `RoleCombinationTests` pins the decision over HTTP: class `Admin` plus method `Support` refuses
  a `Support`-only and an `Admin`-only caller and serves one holding both; one attribute's list is
  any of; two attributes on a query model and two `GateOperations(roles:)` calls require both;
  a role list naming no role refuses startup on `GateOperations`, a resolver and a model member.
- The train side is pinned in the train authorization service's tests (*Role lists from
  separate attributes*), including an attribute on the train's interface, and the operations
  view of the subscriptions by `OperationsGatedByTwoRoleCalls_RequiresBothRoles` in the
  subscription authorization tests.

Not covered: Trax.Mediator's `TrainRegistration.RequiredRoles` still exposes the union, so a
consumer reading it directly sees the old shape.

## Changelog

- **2026-10-01**: Recorded.
