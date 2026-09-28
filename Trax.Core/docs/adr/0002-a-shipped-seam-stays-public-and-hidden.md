---
authors: [Theauxm]
areas: [platform, packaging]
status: accepted
---

# A shipped seam stays public, and is hidden rather than narrowed

`Train.NewMonad()` is `protected virtual` and `ChainRecordedException` is public. Neither is
surface a consumer should program against: `NewMonad()` is the seam `ServiceTrain` uses to hand
the monad its container, and `ChainRecordedException` is the sentinel `Resolve()` returns while a
chain is read. Both shipped public in Trax.Core 1.7.2. They stay public, marked
`[EditorBrowsable(EditorBrowsableState.Never)]` and documented as not for consumers, because
narrowing `NewMonad()` breaks the published Trax.Effect at load time.

## Status

**Accepted.**

## Why this is written down

Both were proposed for `internal` twice, and the obvious reading of the code says they should be.
`Monad`'s constructors are internal, so an override outside Trax can do nothing useful, and a
sentinel documented as never observed has no business being public. The reason not to is
invisible from Trax.Core.

`ServiceTrain` in Trax.Effect overrides `NewMonad()` as `protected override`, through Core's
`InternalsVisibleTo("Trax.Effect")` access to `Monad`'s constructors. Published Effect 1.55.0 was
compiled against `protected virtual`, and its nuspec declares Trax.Core `1.7.2` as a minimum, not
an exact version. A consumer can therefore end up with a newer Core under that Effect. Built
against a Core with `internal virtual NewMonad()`, loading any `ServiceTrain` subclass under
Effect 1.55.0 fails:

```
System.TypeLoadException: Derived method 'NewMonad' in type
'Trax.Effect.Services.ServiceTrain.ServiceTrain`2' from assembly 'Trax.Effect, Version=1.55.0.0'
cannot reduce access.
```

That was run, not inferred (2026-09-27). Effect cannot move first either: `internal override`
does not compile against a `protected virtual` base (CS0507), so the two would have to release in
lockstep and every older Effect would still break against the new Core.

`ChainRecordedException` has no such constraint: no published Trax assembly references it. It is
kept public alongside `NewMonad()` so the pair is settled by one decision instead of leaving a
lone tightening to ship on its own.

## Considered options

**Make both internal in a minor.** Chosen first, then reversed on the evidence above. A breaking
change a consumer never sees at compile time, surfacing as a `TypeLoadException` in every
`ServiceTrain`, is the worst shape a break can take, and a major version would not soften it: the
old Effect would still resolve the new Core.

**Bridge through a new internal seam.** Core would call an `internal virtual CreateMonad()` whose
default calls `NewMonad()`, so an old Effect's override still took effect, while a new Effect
overrode `CreateMonad()` and `NewMonad()` became `[Obsolete]`, to be removed in a major. Rejected
because it adds a second virtual that does the same job, keeps `NewMonad()` public for a whole
major anyway, and its removal would be the same break deferred. Hiding the member buys the part
that matters, that consumers stop seeing it, at no cost.

**Leave both as they were.** Rejected because nothing then told a reader either was not for them.

## Consequences

The two members are public surface Trax does not support. A consumer deriving from plain `Train`
can still override `NewMonad()`, though it can only return what a base built. `ServiceTrain` seals
its override from the next Trax.Effect minor (effect/0009), so a `ServiceTrain` subclass cannot. The
member may be removed when Trax.Core and Trax.Effect next take a coordinated major.

A narrowing of `NewMonad()` is a runtime break for every published Effect, which is why the
member is pinned by a test rather than left to the public API baseline, which shows the diff but
cannot say it is a binary break. Package validation now says so as well, at pack time, but only
against the last Trax.Core release: the test holds the line whatever the baseline is.

## Exemplars

- `ShippedSeamVisibilityTests` pins `NewMonad()` as protected, virtual and hidden, and
  `ChainRecordedException` as public and hidden.
- The XML docs on both members say what they are and point here.

- Package validation (`EnablePackageValidation` in `Directory.Build.props`, run by the pack step
  of the pull request workflow) fails a pack of Trax.Core or Trax.Core.Testing that is not binary
  compatible with `PackageValidationBaselineVersion`, the last release. Narrowing `NewMonad()` is
  one such break. `Trax.Docs/adr/0033` records the rule for every packing repo.

Not covered: package validation compares Trax.Core with its own last release, not with what a
published Trax.Effect was compiled against. A break that already shipped in a Core release is
invisible to it from then on, and a baseline left stale after a release (the step is in the
workspace release procedure, not automated) compares against an older version than consumers run.

## Changelog

- **2026-09-27**: The "Not covered" line said nothing checks binary compatibility. Package
  validation now does, against the last release, so it is named under Exemplars and the gap is
  narrowed to what that comparison cannot see.
- **2026-09-27**: Recorded.
