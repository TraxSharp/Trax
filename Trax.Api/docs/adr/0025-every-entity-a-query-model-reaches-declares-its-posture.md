---
authors: [Theauxm]
areas: [graphql, auth]
status: accepted
---

# Every entity a query model reaches declares its posture

HotChocolate infers an object type, a filter input and a sort input for every navigation a
`[TraxQueryModel]` exposes, binding every public property of the target. The target is then as
reachable as the model: its columns can be selected, and a `where` or `order` over them reveals
their values a comparison at a time. So the entity at the end of a navigation answers the same
question the model does. On an endpoint not gated as a whole by `RequireAuthorization()`, an
entity a query model reaches, through its type or its filter or sort input, must carry
`[TraxAuthorize]` or `[TraxAllowAnonymous]` on its own class, and the host refuses to start,
naming the entity and every navigation that reaches it, when it carries neither. Three rules
follow from treating the inputs as part of that surface:

- **Filter and sort follow the exposed field set.** `BindFields = Explicit` and `ExposeAs` narrow
  a model's `where` and `order` inputs exactly as they narrow its type, on the model's own entry
  field and wherever another model's input reaches it.
- **A gated type's authorization reaches its inputs.** A `where` or `order` that passes through a
  navigation to a type carrying `@authorize` is authorized against that type's directives before
  the query runs, as selecting the type would be.
- **A declared `[TraxAuthorize]` on a navigation target gates its inferred object type**, as it
  does a model's.

## Status

**Accepted.**

## Considered options

**Drop navigations to undeclared entities from the type and the inputs.** Quieter: nothing fails,
and the entity simply is not there. Rejected because it silently removes fields a host may be
using, and because "somebody has to decide" is the rule every other exposed surface already
follows ([0003](./0003-a-type-extension-field-declares-its-own-posture.md), and
`ExposureAuthorizationRule` for trains and models).

**Let a target inherit a gated model's posture.** A target reached only from gated models is
behind their gates in practice, so it could need no marker, as a type-extension field on a gated
parent needs none. Rejected: the entity's own type is a schema member that any later navigation
from an open model reaches too, and the day that navigation is added the gate it was relying on
is gone without anything failing. Strict costs a marker per target and fails closed.

**Drop a gated target's navigation from the inputs of an ungated model.** Simple, and wrong for
the caller who may read the target: an Admin filtering public posts by their gated author would
lose a filter they are entitled to. Authorizing the inputs keeps the filter for them and refuses
it for everyone else.

**Put `@authorize` on the input fields.** HotChocolate's `@authorize` is declared on `OBJECT` and
`FIELD_DEFINITION` only, so an input field cannot carry it, and HotChocolate has no built-in that
extends a type's authorization to the filter and sort inputs built over it. Trax evaluates the
target type's own directives through HotChocolate's `IAuthorizationHandler`, the call its
`@authorize` middleware makes, rather than re-implementing the policy check.

## Consequences

**A host upgrading fails at startup** for every navigation target without a marker, behind gated
models too. The message names the entity and the schema coordinates (`Post.author`,
`PostFilterInput.author`, `PostSortInput.author`) and the three ways out: a marker on the entity,
leaving the navigation out of the exposed field set, or gating the endpoint.

**The check reads the built schema and the model's `DbContext`.** A navigation the exposed field
set leaves out needs no marker, and a class EF Core maps as owned is part of the entity that holds
it and is walked through rather than reported. A `DbContext` the validator cannot resolve makes
every reached class count as an entity, so the check fails closed.

**A filter or sort through a gated navigation costs one authorization evaluation per gated type
it touches.** Inputs the caller does not use cost nothing.

## Exemplars

- `QueryModelNavigationPostureTests` pins the refusal and what it names, the gated-endpoint
  exemption, the conflict, the navigation left out of the field set, the owned value, and a gated
  target refusing an anonymous read, filter (inline and as a variable) and sort.
- `QueryModelFilterSortFieldSetTests` pins that `Explicit` and `ExposeAs` narrow the inputs on the
  model's own field and through a navigation.
- The query-model authorization end-to-end suite drives the input authorization over HTTP with
  API keys: anonymous and Player callers refused through `publicBooks.linkedOwnedBook` and
  `owners.books.some`, Admin served.

Not covered: a host-supplied filter or sort override (`AddFilterType`/`AddSortType`) is used as
given, so it can offer fields the type does not; the reach check still walks it, and the input
authorization still applies to it. A `ConfigureSchema` callback that removes the entry-field
middleware is not detected.

## Changelog

- **2026-09-30**: Recorded.
