---
authors: [Theauxm]
areas: [platform]
status: accepted
---

# Plain Train stays a consumer base, and its Run stays overridable

`Train<TIn, TOut>` is a supported base class for code that uses Trax.Core without Trax.Effect, and
`Run(input, ct)` on it stays `public virtual`. A class that overrides `Run` and never reaches
`Junctions()` runs something other than its declared chain, so `DeclaredChain()`, and any check
built on it, describes a chain that does not run. That is accepted and documented rather than
prevented.

## Status

**Accepted.**

## Why this is written down

Because the gap is visible from the code and looks like an oversight. `ServiceTrain` closes the
same gap for itself: its `Run` overloads are sealed or non-virtual (effect/0009), and the startup
check the mediator runs covers only trains the mediator registers, which are service trains. What
is left is a train deriving from plain `Train`, used by a Core-only consumer who runs it directly.
Nothing in Trax verifies that train's chain unless the consumer calls `DeclaredChain()` and
`ChainVerification.Verify` themselves, so an override that skips the chain costs them a check they
opted into, not one Trax promised.

`Run` also cannot simply stop being virtual. Published Trax.Effect overrides it in `ServiceTrain`,
and its nuspec declares Trax.Core as a minimum version, so a newer Core with a non-virtual `Run`
would make every published `ServiceTrain` fail to load, the same shape of break core/0002 records
for `NewMonad()`.

## Considered options

**An analyzer error on a `Run` override.** Rejected for now. Trax.Core's analyzer is deprecated
(core/0001), an analyzer only runs where it is referenced, and refusing the override outright
would break Core-only consumers who override `Run` for reasons Trax has not surveyed, such as
wrapping the call in their own logging or retries while still calling `base.Run`.

**Make `Run` non-virtual at the next major.** Rejected. It needs Trax.Core and Trax.Effect to
release in lockstep, and a major does not protect an older Effect that resolves the newer Core:
it would still fail to load. The option remains available if Core and Effect ever take a
coordinated major for another reason.

**Leave it undocumented.** Rejected: a reader of `Train.Run` had no way to tell that overriding it
steps outside what the chain check can see.

## Consequences

A plain-`Train` subclass that overrides `Run` should call `base.Run` if it wants its declared chain
to be the chain that runs. One that does not is outside Trax's guarantees for chains
(`Trax.Docs/adr/0016`). The `Train` class and `Run` document this in their XML docs, and the SDK
reference page for `Run` says it.

## Exemplars

- `PlainTrainRunStaysVirtualTests` pins `Train.Run` as virtual and not sealed, so the published
  Trax.Effect's override keeps loading.
- [Run / RunEither](/docs/sdk-reference/train-methods/run) states the consequence for a train
  that overrides `Run`.

Not covered: nothing detects a `Run` override that skips the chain. The test pins the modifier,
not how a subclass uses it.

## Changelog

- **2026-09-27**: Recorded.
