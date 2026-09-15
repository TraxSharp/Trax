---
authors: [Theauxm]
areas: [graphql, auth]
status: accepted
---

# A type-extension field on an anonymous parent declares its own posture

Trax refuses to boot when an ungated endpoint exposes a train or a `[TraxQueryModel]` entity
declaring neither `[TraxAuthorize]` nor `[TraxAllowAnonymous]`. That census cannot see an
`[ExtendObjectType]` resolver, because such a field is added to the type by HotChocolate and
never passes back through Trax, so a resolver bolted onto a `[TraxAllowAnonymous]` entity is
public without anyone having decided it should be. A field whose parent is anonymous, or whose
parent is a root type, must carry `[TraxAuthorize]` or `[TraxAllowAnonymous]`, and the host fails
at startup when it carries neither.

Inheritance is what makes this narrow enough to ship. A field on a gated parent is already
behind that parent's `@authorize`, so it needs no marker, and a field on a type carrying
neither marker reaches the schema only inside a train's output, where the train's posture
governs. Only two rows have to declare: an anonymous parent, which inherits nothing, and a
root type, which has no parent at all.

## Status

**Accepted.**

## Considered options

**Requiring a marker on every type-extension field.** Uniform, and wrong: it fails the host
for every consumer on upgrade, in exchange for repeating a gate the parent type already
applies. The three-way rule keeps the blast radius to fields that really do inherit nothing,
and it stays honest, because the day a parent gains `[TraxAllowAnonymous]` its fields move
into the must-declare row and the host fails.

It is still a breaking change, and the size of it is worth stating: every hand-written field
on a root type has to declare, and that is not rare. Landing the census in Trax's own test
suite required a marker on nine of them. Each was deliberately anonymous, each took
`[AllowAnonymous]`, and the diff is the argument for the rule: nothing about those fields
said so before.

**Requiring HotChocolate's `[Authorize]` and `[AllowAnonymous]` instead.** What this ADR
originally decided, and it was wrong. Those attributes already apply to a resolver, so the census
could have shipped without touching `Trax.Effect` at all, and the cost looked like a release
window: widening Trax's attributes means `Trax.Effect` releases before `Trax.Api` can enforce the
widening. That window is the ordinary shape of a cross-repo change here. What it bought was the
first place in Trax where a consumer has to write HotChocolate's vocabulary for Trax's own check
to pass, which is what `[TraxAuthorize]` exists to prevent. The vocabulary is now Trax's, the
foreign attributes are refused by name, and
[effect/0004](../../../Trax.Effect/docs/adr/0004-trax-owns-the-authorization-vocabulary.md)
records that decision where the attributes live.

**Reading the census off `GraphQLConfiguration.AdditionalTypeExtensions`.** Cheaper, and blind
in the one direction that matters. `ConfigureSchema` hands the consumer the whole
`IRequestExecutorBuilder`, so a type extension can reach the schema without passing through
`AddTypeExtensions`, and a census built from the registration list would not know it exists.
Resolving fields off the merged object type sees them however they were registered, which is
the same escape hatch [0001](./0001-a-misconfigured-host-fails-at-startup.md) closes with a
post-build validator.

## Consequences

**It fails closed.** A field whose parent posture cannot be resolved is treated as an anonymous
parent and must declare. The cost is a consumer occasionally writing a marker on a field that
did not need one, which is the right direction for the error to point.

**`ExposureAuthorizationRule` does not change, and is reused with one deliberate exception.**
It is already a pure function of `(hasAuthorize, hasAllowAnonymous, endpointGated)`, so the
marker decision is made by the same code for a train, an entity and a resolver. The exception
is `AnonymousUnderGate`: an entity's `[TraxAllowAnonymous]` under a gated endpoint is reported,
because the entity gate is all or nothing and the endpoint already answered, while a field's
`[AllowAnonymous]` is not, because on a role-gated parent it still means something, namely any
authenticated caller rather than only the role.

**A class-level `[TraxAuthorize]` on a type extension gates that extension's fields.** This is
the part owning the attribute buys outright. HotChocolate applies a class-level attribute to the
type being extended, so on a `[TraxAllowAnonymous]` entity it re-locks the entity and on a root
type it sets the posture of every operation in the schema. Trax applies its own to the fields the
extension contributes, which is what someone writing it there means.

**Trax emits the directive, so the attribute is a gate and not an annotation.**
`TypeExtensionExposureInterceptor` reads the attributes at `OnBeforeRegisterDependencies`, on the
extension's own configuration, which is early enough for HotChocolate to turn `@authorize` into
resolver middleware. `OnBeforeCompleteType`, where the census runs, is too late for that and is
the only place the merged parent is known, which is why the two phases sit at different hooks.

**Return type is not an exemption.** A field returning a gated entity still has to declare.
What the field returns is not what the field does, and a census that reasons about return
types has to keep re-deriving a second type's posture to answer a question about the first.

## Exemplars

- `TypeExtensionExposureRuleTests` pins the decision matrix, all 24 combinations of parent
  posture, the two attributes and the endpoint posture, plus the divergence from
  `ExposureAuthorizationRule` described above.
- `TypeExtensionExposureTests` drives it through a real host: which fields the census sees, what
  it resolves their parent to be, and that the host refuses to start. It includes the case that
  justifies reading the merged type, a type extension registered through `ConfigureSchema`, which
  never reaches `AdditionalTypeExtensions`, and the refusal of HotChocolate's attributes.
- `ResolverAuthorizationTests` proves the emitted directive is a real gate over HTTP:
  `[TraxAuthorize(Roles = ...)]` on a resolver refuses an anonymous caller and a caller without
  the role, serves the role holder, and leaves a `[TraxAllowAnonymous]` sibling open.
**Enforced elsewhere:** NoForeignAuthorizationAttributesTests keeps HotChocolate's attributes out
of this repo's own code, allowlisting the translation layer that emits the directive. It enforces
the vocabulary decision rather than this one, which is recorded centrally as
[docs/0013](../../../Trax.Docs/adr/0013-trax-owns-the-vocabulary-for-its-own-concepts.md).
- [Architecture Guards](/docs/reference/architecture-guards) is the rule this produces.

Not covered:

- A field built from a lambda in a schema callback has no member to carry an attribute, so the
  census skips it. Trax's own `discover` and `operations` entry fields are built this way and
  are gated by the code that builds them.
- A resolver reached through a route Trax does not own at all, such as a minimal-API endpoint
  over the same data, is outside the schema and outside this rule.
- Nothing checks that a marker is the *right* one. `[AllowAnonymous]` on a field that should
  have been gated satisfies the census, which asks only that somebody decided.

## Changelog

- **2026-09-15**: Reversed the vocabulary decision. The markers are `[TraxAuthorize]` and
  `[TraxAllowAnonymous]`, widened to methods in
  [effect/0004](../../../Trax.Effect/docs/adr/0004-trax-owns-the-authorization-vocabulary.md), and
  HotChocolate's are refused. Trax emits the directive itself, which also lets a class-level
  attribute mean the extension's fields rather than the extended type.
- **2026-09-15**: Accepted and implemented. Recorded the blast radius honestly (root-type fields
  are not rare), the `AnonymousUnderGate` divergence, and the class-level-attribute case.
- **2026-09-15**: Recorded.
