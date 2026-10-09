---
authors: [Theauxm]
repos: [core, effect, mediator, scheduler, dashboard, api, cli, samples]
areas: [testing, packaging]
status: accepted
---

# Property tests use CsCheck, in test projects only

A law (every node id is unique, inserting a step moves no other id, the same graph always
serialises to the same text) is checked over generated cases, not over the three examples
someone thought of. Trax writes those tests with [CsCheck](https://github.com/AnthonyLloyd/CsCheck),
and only a test project may reference it.

## Status

**Accepted.**

## How a property test is written

- **Generated cases over hand-picked ones, for a law.** A hand-picked case still has its place
  for a specific scenario; a statement that has to hold for every input is a property.
- **Shrinking does the debugging.** When a property fails, CsCheck shrinks the input to a
  smallest one that still fails and reports it, so the failure arrives as a case a person can
  read rather than as the random one that found it.
- **The seed is printed and pinned.** CsCheck prints the seed of the shrunk case. Before fixing
  the code, pin it as its own test (`Sample(..., seed: "...")`), so the case keeps running after
  the generators change and the random search moves elsewhere.

## Considered options

**FsCheck.** The older library, and the obvious name. It is written for F# first, and its
shrinking belongs to an `Arbitrary` rather than to the generator, so a generator composed in C#
needs a shrinker written beside it. CsCheck's generators compose with `Select` and shrink with no
extra code, which is what the graph and invariant laws need.

**Hand-rolled random loops.** A `Random` and a `for` loop find the bug and then print a 400-step
input nobody can read, with no seed to replay it. Shrinking is the part not worth writing again.

## Why a licence that is not MIT is fine here

CsCheck is Apache-2.0. Trax is MIT, and MIT forever, so the question is whether anything of
CsCheck's reaches a consumer. It does not: a test project is never packed, so nothing published
to nuget.org carries CsCheck, declares it as a dependency, or links to it. Apache-2.0 places its
obligations (keeping the notice, stating changes) on redistribution, and a test dependency is
never redistributed. AwesomeAssertions is the precedent: Apache-2.0 as well, and in every test
project already.

That reasoning holds only while the boundary holds, which is why the boundary is the guarded
part. A shipped package referencing CsCheck would make it a dependency of every consumer, and a
`Trax.*.Testing` package is shipped: those packages carry NUnit because their fixtures are the
product (`0011`), but they ship fixtures, not properties, so the exception does not extend to
CsCheck.

## Exemplars

**Enforced elsewhere:** `NoTestFrameworkInSrcTests` in each of the eight code folders' `Tests.Meta`
projects. Its `OnlyTestProjects_ReferenceCsCheck` test reads every `.csproj` in the folder, under
`src/` or anywhere else, and fails on a CsCheck reference in any project that does not set
`<IsTestProject>true</IsTestProject>`; a sibling test proves the scan flags a library and spares a
test project. The first use is `ChainGraphPropertyTests.cs` in Trax.Core's unit tests.

Not covered: the guard reads `PackageReference` items in project files, not one added through a
`Directory.Build.props` or `.targets`. Nothing checks that a law is tested as a property rather
than by examples, or that a failing seed was pinned before it was fixed; both are review's job.

## Changelog

- **2026-10-07**: Recorded.
