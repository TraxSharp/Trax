---
authors: [Theauxm]
areas: [testing, samples]
status: accepted
---

# The samples adopt the shipped guards the way a consumer would

`BookwormArchitectureGuards` contains no test bodies. It subclasses the abstract fixtures
published in `Trax.Effect.Data.Testing`, `Trax.Api.GraphQL.Testing` and
`Trax.Mediator.Testing`, supplies configuration, and lets the inherited `[Test]` methods be
discovered in this assembly. That is exactly the adoption path a consumer follows.

## Status

**Accepted.**

## Why this is written down

Because it is the only place the fixtures are adopted across a package boundary.

Each package already subclasses its own fixture in a self-test:
`DomainDataLayerGuardFixtureSelfTest` in Trax.Effect, `CrossSchemaGuardFixtureSelfTest` in
Trax.Api, `TrainGuardFixtureSelfTest` in Trax.Mediator. Those cover subclassing, option
supply, and discovery of the inherited `[Test]` methods, and an abstract member changing shape
breaks them at compile time. What they cannot cover is the packaging: each one runs inside the
repo that ships the fixture, against that repo's own `ProjectReference`s, so the compiler
resolves the fixture, its options type and everything they touch from source whether or not
any of it reaches the `.nupkg`.

Trax.Samples has no cross-repo `ProjectReference`. It reaches all three fixtures only through
a `PackageReference` resolved from the feed, so a type left `internal`, a member missing from
the published assembly, or a file left out of the pack fails here and nowhere else.

## Consequences

**The flagship sample carries a test project that exists for the framework's benefit**, not
to demonstrate anything to a reader of the sample. That is a slightly odd thing for a sample
to do, and it is deliberate.

**A guard package without a Bookworm subclass is untested from the outside.** Adding a new
`*GuardFixture` to any repo means adding its subclass here, or the adoption path for that one
is unproven.

**Bookworm's own architecture is therefore constrained** by the guards it adopts: one schema
per context, companion interfaces, cross-schema edges through the manifest. That is the
point, since a sample that could not satisfy them would be a poor demonstration.

## Exemplars

**Enforced elsewhere:** the `[Test]` methods live in the packages, not here.
`DataLayerGuards` and `DomainDataLayerGuardFixture` ship from `Trax.Effect`,
`CrossSchemaGuards` and `CrossSchemaGuardFixture` from `Trax.Api`, and `TrainGuards` with
`TrainGuardFixture` from `Trax.Mediator`. `BookwormArchitectureGuards.cs` in this repo is
three subclasses and a configuration block.

Not covered: nothing checks that every shipped `*GuardFixture` has a subclass here. A new
fixture added upstream with no Bookworm adopter is untested from the consumer side, and
nothing says so.

## Changelog

- **2026-09-11**: Replaced the justification. All three packages do ship a fixture-subclass
  self-test, so the claim that nothing else exercises subclassing and discovery was false. What
  is only exercised here is adoption across a real `PackageReference` rather than the shipping
  repo's own project references.
- **2026-09-11**: Recorded.
