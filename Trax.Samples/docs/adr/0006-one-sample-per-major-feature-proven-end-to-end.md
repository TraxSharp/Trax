---
authors: [Theauxm]
areas: [samples, testing]
status: accepted
---

# One sample per major feature, each proven end to end

Each sample is the exemplar for one major Trax feature: simple enough to show that feature and
little else, complete enough to run, and proven by E2E tests against the real host. A feature
with no sample is proven by an E2E or integration test in the repo that ships it. The README's
feature-coverage table lists every major feature and the test class that proves it, whichever
repo holds it. Trax.Core gets no sample, because every sample already runs on it. This replaces
the earlier rule that every feature and fix must appear in a sample.

## Status

**Accepted.** Decided on 2026-09-29 and 2026-10-02. The earlier rule was never recorded as an
ADR, so there is nothing to mark superseded.

## Considered options

**Every feature and fix in a sample.** It turned samples into catch-alls: GameServer, JobHunt
and TestRunner each showed many features at once, none of them simply, and their suites rotted
because a fix in one feature broke a sample about something else. A reader looking for how to
use one feature had to dig it out of an application.

**No table, samples only.** A feature without a sample would then have no visible proof from
here, and nothing would say whether it was tested at all.

## Consequences

Adding a feature does not oblige anyone to add a sample. It obliges a row in the table, pointing
at the test that proves the feature, here or upstream. A new sample needs an E2E suite and a
row naming one of its classes, or, for a sample whose proof is its feature files, a row linking a
feature file the suite runs through Reqnroll. A feature with no end-to-end test anywhere is written as
`no E2E` in its row, so the gap is visible rather than implied away.

## Exemplars

- `FeatureCoverageTableTests` fails when the README has no feature-coverage table, when a row
  names no test class and does not say `no E2E`, when a class named for this repo is not declared
  in the file its row links, when a linked feature file is not under a sample's `features/` or
  `specs/`, is not the link text's file, is missing or declares no `Feature:`, and when an E2E
  project under `tests/` is named by no row. A project that compiles a linked feature file as a
  `ReqnrollFeatureFiles` item counts as named.
- [Samples](/docs/samples) is the published index of the samples this produces.

Not covered: a row naming a class in another Trax repo is checked for shape only (a known repo,
a path under `tests/`, a file named after the class), not for existence. CI checks out this
repository alone, and a local sibling checkout sits on whatever branch its developer left it on,
so an existence check would pass or fail for reasons outside this repo. Nothing checks that a
sample stays about one feature, or that a row's test really proves the feature it is listed
against; that is review.

## Changelog

- **2026-11-08**: Amended: a row may link a sample's feature file, with the file name as its
  text, and an E2E project counts as named when a row links a feature file it compiles. The
  Bakery's row links its two feature files instead of naming them in prose. The link text is not
  matched to the file's `Feature:` title, which is prose and changes without the file moving.
- **2026-10-03**: Recorded.
