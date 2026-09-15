---
authors: [Theauxm]
repos: [core, effect, mediator, scheduler, dashboard, api, cli, samples]
areas: [platform, naming]
status: accepted
---

# Trax owns the vocabulary for its own concepts

Trax builds on other libraries and says so. What a consumer writes is a different question: where
Trax has a concept of its own, the consumer declares it in Trax's words, and Trax translates into
whatever the library underneath needs. A surface that makes the consumer write the library's type
instead is a leak, and the abstraction is then worth less than the dependency it was hiding.

This is not a rule against third-party types. It is a rule about which of them reach the consumer.

## Status

**Accepted.**

## Which concepts are Trax's

Two tests, and they agree in the cases seen so far.

**Does the concept exist in Trax independently of the library?** `[TraxAuthorize]` applies to a
train, which never reaches a GraphQL schema. Authorization posture was a Trax concept before
GraphQL existed here, so the GraphQL surface is borrowing it, and the borrow has to be expressed
in Trax's words. An `[ExtendObjectType]` is HotChocolate's own modelling primitive: Trax has no
independent notion of a type extension, so a Trax-named wrapper would be a rename with nothing
behind it.

**Would swapping the library change consumer code?** `[TraxAuthorize]` survives a change of
GraphQL server. `[ExtendObjectType]`, `[Parent]` and `[Subscribe]` cannot and should not pretend
to. Neither should `[Column]` or `[Key]`, which are the data annotations' own.

Applied honestly the tests exclude most third-party types a consumer writes today, and that is the
point: a principle that bans everything gets ignored the first time it is inconvenient.

## The signal that this is happening

**A workaround or a diagnostic written to explain a third-party type's behaviour to Trax users.**
That is the tripwire, and it is what was missed the first time. While making type-extension fields
declare a posture, Trax briefly required HotChocolate's `[Authorize]` on a resolver, then found
that a class-level `[Authorize]` applies to *the type being extended*, which re-locks a
`[TraxAllowAnonymous]` entity and, on a root type, sets the posture of every operation in the
schema. The response was a diagnostic telling consumers to work around it. Writing that diagnostic
was the moment to stop: teaching users somebody else's semantics, and apologising for them, means
Trax should own the type. Once it did, `[TraxAuthorize]` on an extension class simply gates that
extension's fields and the diagnostic was deleted.

## Considered options

**Leaving it to review.** What was in place, and it did not hold: the leak was introduced, argued
for in an ADR, and shipped, because the constraint that produced it (`[TraxAuthorize]` did not
target methods) was discovered as a fact and treated as terrain rather than as a choice.

**Banning every third-party type from consumer-facing code.** Unworkable. It ends at reimplementing
HotChocolate and Entity Framework, and a rule nobody can follow is not enforcement.

**An analyzer instead of a guard.** Stronger, because it would fail at compile time in consumer
projects rather than in Trax's own test suites. Not done: it means a new analyzer package and a new
diagnostic vocabulary, and the guard covers Trax's own code today, which is where the leak
happened. Worth revisiting if a consumer-facing version is wanted.

## Consequences

**The list is per repo, not global.** `VocabularyGuards` in Trax.Core.Testing takes the banned
vocabularies as an argument, because only the repo knows which concepts are its own. A repo with no
overlapping dependency passes an empty list and the guard finds nothing.

**Only two repos can currently offend.** Trax.Api and Trax.Samples are the only ones referencing
HotChocolate; the rest cannot write its attributes because they do not reference it. Wiring the
guard into repos that cannot offend would produce passes that mean nothing, which is the failure
the guard's inspected count exists to expose.

**The translation layer is exempt, by name.** Code that constructs the library's type to speak to
the library is the allowed direction, and it is allowlisted with a reason rather than pattern
matched, so an exemption is a decision somebody wrote down.

## Exemplars

- [Authorization](/docs/authorization) states the rule consumers see, including why HotChocolate's
  `[Authorize]` and `[AllowAnonymous]` are refused and how to migrate.

**Enforced elsewhere:** Trax.Core.Testing.Guards.VocabularyGuards is the shipped check, covered by
VocabularyGuardsTests in Trax.Core; NoForeignAuthorizationAttributesTests in Trax.Api keeps
HotChocolate's authorization attributes out of that repo, allowlisting the translation layer that
emits the directive; Trax.Effect.Attributes.TraxAuthorization refuses them at runtime, so a
consumer's host fails at startup rather than silently declaring a posture Trax cannot read. The
repo-scoped decisions are effect/0004 and api/0003.

Not covered:

- Nothing checks consumer code at compile time. A consumer learns when their host starts.
- The guard matches the list it is given. A new dependency whose vocabulary overlaps Trax's is
  invisible until somebody adds it.
- Six of the eight code repos are not wired, because they do not reference the library in
  question. The day one of them takes such a dependency, it adds the guard with its own list.

## Changelog

- **2026-09-15**: Recorded.
