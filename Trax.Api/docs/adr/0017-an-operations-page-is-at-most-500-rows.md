---
authors: [Theauxm]
areas: [graphql]
status: accepted
---

# An operations page is at most 500 rows, and take 0 returns one

Every paged read in the `operations` namespace (`manifests`, `executions`,
`executionChildren`, `logs`, `deadLetters`, `workQueues`, `manifestGroups.groups`) clamps
`take` to 1 through 500 and treats a negative `skip` as 0. The page it returns reports the
effective `take` and `skip`, not the requested ones. The bound is `OperationsPageBounds.MaxPageSize`.

`skip` is capped too, at 10,000 (`OperationsPageBounds.MaxSkip`), and a deeper one is
**refused**, not clamped: the field fails with `TRAX_SKIP_TOO_DEEP`, whose message and
`maxSkip` extension point the caller at the `afterId` keyset cursor, which reaches every row.

## Status

**Accepted.**

## Considered options

**Refusing an out-of-range argument.** Rejected for `take`: a list read is not a command, and a
client asking for more than a page is served a page and told its size through `take` on the
result. Accepted for `skip` above the cap, because there is no nearby answer to serve: clamping
the offset would return a different page from the one asked for, as if it were the right one.
HotChocolate's own paging refuses a page size over its maximum the same way, with an error
rather than a smaller page.

**No cap on `skip`.** The original decision. An offset makes the database read and discard
every skipped row, so one request near the end of a table of millions costs a full scan of it,
and repeated, that is a cheap way to load the database from an account that may read the
namespace. 10,000 rows is twenty pages of the largest size, further than an operator pages by
hand, and still cheap to skip; past it, `afterId` reads the next page from the index.

**`take: 0` returning an empty page.** That was the behaviour before the clamp, as an accident
of `LIMIT 0`. Rejected to match `Trax.Scheduler`'s `OperationsService`, whose paged reads
clamp to 1 through its own `MaxPageSize` of 500. The Api resolvers are moving onto that
service, and the two surfaces must answer the same request the same way. A caller that wants
only `totalCount` still gets it, with one row beside it.

**A larger cap.** No caller pages past 500: the Blazor dashboard reads the database directly,
not through this surface, and the GraphQL defaults are 25. Keyset paging through
`afterId` reaches every row at 500 a page.

## Consequences

The Api's constant and the Scheduler's `OperationsService.MaxPageSize` are two numbers that
must agree. Changing one without the other makes the same read answer differently depending
on which layer served it.

## Exemplars

- `OperationsPageBoundsTests` seeds 501 rows behind each paged read and pins that
  `take: 100000` returns 500, `take: 0` and a negative `take` return one row, a negative
  `skip` reads as 0, `skip: 10000` is served, and `skip: 10001` is refused with
  `TRAX_SKIP_TOO_DEEP`.
- The stress suite's `Executions_DeepestOffset_WithinBudget_AndDeeperIsRefused` (in
  `AdminEndpointStressTests.cs`) measures the deepest served offset over millions of rows.

Not covered: nothing checks that the Api's cap equals the Scheduler's; they live in different
packages. The persisted-operations admin reads (`persistedOperations`) are not part of the
operations namespace and are not clamped by this.

## Changelog

- **2026-10-01**: `skip` capped at 10,000, a deeper one refused with `TRAX_SKIP_TOO_DEEP`
  pointing at `afterId`. Reverses the original's no-cap stance for `skip`; `take` still clamps.
- **2026-09-27**: Recorded.
