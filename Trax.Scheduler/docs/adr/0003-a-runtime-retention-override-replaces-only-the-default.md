---
authors: [Theauxm]
areas: [scheduling]
status: accepted
---

# A runtime retention override replaces only the default, never a per-train retention

Metadata cleanup takes a retention period per whitelisted train, and that period is also
editable at runtime through the dashboard and `updateSchedulerConfig`. The runtime value
replaces `MetadataCleanupConfiguration.RetentionPeriod`, the default that trains fall back to,
and leaves a retention passed to `AddTrainType` alone.

## Status

**Accepted.**

## Why this is written down

Because the other answer is the obvious one. A field labelled "Retention Period" on a settings
page reads as *the* retention, and the first person to notice that editing it did not shorten
one train's rows will read that as a bug and fix it.

It is not a bug. A per-train retention is written in code, in a place that gets reviewed, and
the reason it exists is usually that the row holds something whose lifetime is not an
operational preference: a mutation train's input is a customer's name and address, kept thirty
days so a rerun can replay it and gone afterwards. The dashboard field is an operational knob,
reachable by anyone with access to the page and with no review in front of it. Letting the knob
silently shorten the reviewed value gets the authority backwards.

The alternative, the override winning over everything, is simpler to explain in one sentence
and worse in every situation that motivated the feature. It makes a per-train retention
something you can only rely on until somebody edits an unrelated-looking field, which is the
same as not being able to rely on it.

This also happens to be what the code already did. `SchedulerConfigBootstrapHostedService` and
`OperationsService.UpdateSchedulerConfigAsync` each write `RetentionPeriod` and nothing else,
so the decision cost no code, only the risk that a later change folds the two together without
knowing there was a decision here.

## Consequences

**The dashboard field means less than its label says, and the label has to carry that.** It is
documented on the page and in `add-metadata-cleanup.md`. A reader who does not find that will
be confused exactly once.

**There is no runtime way to shorten a per-train retention.** Deleting sooner than a train's
declared retention needs a deploy. For the case this exists for, data kept to a stated period,
that is the correct cost; for someone using per-train retentions purely to manage table size it
is a real inconvenience, and the answer is to leave those trains on the default.

**The internal scheduler trains are on the default and cannot leave it.** `AddTrainType`
refuses a retention for one rather than ignoring it, so the override always reaches them. They
are the highest-volume writers in the table and an unbounded one is the outage this whole
feature area exists to prevent.

## Exemplars

- `MetadataRetentionTests` runs a cleanup with one train on the default and one on thirty days,
  changes `RetentionPeriod` between the seed and the sweep, and asserts the second row survives.
  The same fixture covers the admin-train refusal.
- [AddMetadataCleanup](/docs/sdk-reference/scheduler-api/add-metadata-cleanup) is the rule this
  produces.

Not covered: nothing asserts that `SchedulerConfigBootstrapHostedService` and `OperationsService`
write only `RetentionPeriod`. The guard exercises the junction with a configuration those two
could have produced, so a change that made either of them write per-train values would pass it.
That is the shape a regression would take, and it would have to be caught in review.

## Changelog

- **2026-09-25**: Recorded, with the per-train retention feature it describes.
