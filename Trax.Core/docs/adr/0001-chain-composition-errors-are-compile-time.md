---
authors: [Theauxm]
areas: [platform]
status: accepted
---

# A mis-composed chain does not compile

A Roslyn analyzer ships as the separate, opt-in `Trax.Core.Analyzers` package. It triggers
on a `Resolve()` call, walks back through the fluent chain that produced it, simulates what
each junction puts into memory, and raises an **error** when a junction asks for a type
nothing has produced (`CHAIN001`) or when the train's return type is never produced
(`CHAIN002`).

Both are `DiagnosticSeverity.Error`, not warnings. The build stops.

## Status

**Accepted.**

## Considered options

**Validate at run time, when the train is first executed.** This is what the memory
dictionary would do anyway: a missing type is a lookup that finds nothing. It was rejected
because the failure lands at the worst moment. A chain is wired in a constructor and
executed somewhere else entirely, often behind a scheduler or a GraphQL resolver, so the
exception surfaces far from the line that caused it and only on the code path that runs.

**Validate at registration**, walking the chains once at startup. Better than run time, and
still worse than the compiler: it needs the host to boot, it cannot say which line is wrong,
and a train nobody registered is never checked at all.

**Warning instead of error.** Rejected because there is no legitimate way to continue. A
junction whose input is not in memory cannot run; allowing the build to proceed only moves
the failure back to run time, which is the option already rejected. A false positive is the
one case where continuing is legitimate, and a pragma settles that at the call site without
making every true positive advisory.

## Consequences

**Opting in is the consumer's choice, and the default is off.** `Trax.Core` ships
`lib/net10.0/Trax.Core.dll` alone and takes no dependency on the analyzer, so a consumer
gets it only by adding `Trax.Core.Analyzers` themselves. That is deliberate: the analyzer
raises errors, and an error nobody asked for is a broken build in somebody else's
repository. Inside this solution it is propagated to every project by `Directory.Build.props`
as a `ProjectReference` with `OutputItemType="Analyzer"`, which is why it looks automatic
from in here and is not.

**A false positive costs a consumer their build**, and the analyzer cannot rule one out. It
abandons a chain only when it cannot parse it at all: the receiver is not a `Monad<,>`, the
fluent chain does not parse, or it does not start with `Activate()`. Anything else it cannot
resolve is skipped one step at a time, and a skipped step is the case that bites: its `TOut`
never enters the simulated memory, so a later junction or the `Resolve()` check reports
CHAIN001 or CHAIN002 against memory the running chain would have filled.

The skip that matters is a junction named by a type parameter rather than a concrete type.
`GetSingleTypeArgument` takes the first type argument only when it is an `INamedTypeSymbol`,
and a generic method's own parameter is an `ITypeParameterSymbol`, so a helper along the lines
of `Run<TJunction>()` that chains its `TJunction` is skipped while the runtime resolves it
normally. A chaining method the walk does not recognise drops out the same way, as does an
input satisfied through a sibling interface, which `memory.Contains` misses. What is *not* a
false positive is a junction with no `IJunction<TIn, TOut>` at all: `Chain<TJunction>()` is
constrained to `class`, so it compiles, but `ExtractJunctionTypeArguments` then throws at run
time for the same reason the analyzer could not resolve it. The escape hatch is
`#pragma warning disable CHAIN001` at the call site, or `<NoWarn>` for a project built on a
pattern the analyzer cannot follow, and [Analyzer](/docs/core/analyzer) documents both. That
cost is what the opt-in packaging buys back: a consumer unwilling to reason about the
analyzer's blind spots does not reference it, and nothing changes for them.

**It only sees what it can read syntactically**, and it does not guess. The chain must
terminate in `Resolve()` and start with `Activate()`. A chain assembled dynamically, or
across a method boundary, is invisible to it, and those cases fall back to the runtime
behaviour the analyzer exists to avoid.

## Exemplars

- `TrainChainAnalyzerTests` drives the analyzer over source fixtures and asserts the two
  diagnostics fire where expected and stay quiet where the chain is sound.
- [Analyzer](/docs/core/analyzer) documents the two diagnostics for users.

Not covered: nothing asserts the analyzer is packed into the NuGet output at the right path,
or that `Trax.Core` continues **not** to depend on it. A packaging regression in either
direction is silent: one ships a package whose analyzer never loads, the other turns an
opt-in error source on for every consumer at once. Nothing drives a chain through a junction
the analyzer cannot resolve either, so the skip path above and the false positives that
follow from it go unasserted.

## Changelog

- **2026-09-11**: Corrected the claim that the analyzer stays silent whenever it cannot see
  the whole chain. An unresolvable junction is skipped and the walk continues, so false
  positives are possible and are suppressed with a pragma.
- **2026-09-11**: Recorded.
