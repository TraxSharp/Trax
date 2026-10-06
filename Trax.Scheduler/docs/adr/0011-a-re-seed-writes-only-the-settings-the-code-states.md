---
authors: [Theauxm]
areas: [scheduling]
status: accepted
---

# A re-seed writes only the settings the code states

Every host start schedules its manifests again. The schedule, the input and the train always
come from code. The settings an operator can also change at runtime (a manifest's enabled flag,
retries, timeout and priority, and a group's priority, limit and enabled flag) are written only
when the scheduling options state them; an unstated one keeps whatever the database holds. Two members of one group that state
different values for the same group setting fail the build.

## Status

**Accepted.** Amended 2026-09-30: a manifest's retries, timeout and priority are written only
when stated too.

## Why this is written down

Because the upsert used to write every field, and the result looked like a feature. A manifest
disabled from the dashboard as a kill switch came back on at the next deploy, crash or
scale-out. A group limit set by an operator was reset to whatever the code said, usually
nothing. In a group shared by several manifests the last one seeded decided the group's priority,
limit and enabled flag for all of them, and a member that joined with `.Group("name")` alone
cleared the limit another member had set.

Two answers were weighed. **The operator always wins**: code only creates rows, never updates
these settings. That makes a change to a limit in code invisible to every existing deployment,
which is the same bug from the other side. **Code wins for what it states** (this ADR): the
code is still the source of truth for anything it says, and silence in code means "not my
concern", so the dashboard owns it.

The same rule forces the build-time check. If two members both state a group setting and
disagree, one overwrites the other at every start and seeding order picks the winner, so the
builder refuses it and names both.

## Consequences

**Removing a stated setting from code leaves the last value in place.** Deleting
`.Enabled(false)` does not re-enable the manifest; stating `.Enabled(true)` does. The docs say
so under "What a Restart Rewrites".

**A group's priority follows the manifest's only where the group is the manifest's own**: no
group name, or a named batch's own group. In a shared group the first member seeded sets the
initial priority and after that only an explicit group priority changes it. A scheduled run is
queued at its manifest's priority, so a manifest's priority still orders work inside a shared
group.

**A manifest's retries, timeout and priority moved to the operator's side** once an edit path
for them existed (an update-manifest action writes `MaxRetries`). Left unstated, a new manifest
still takes the scheduler's `DefaultMaxRetries`, no timeout and priority 0, but an existing one
keeps its value, so changing `DefaultMaxRetries` no longer rewrites manifests that already
exist; stating `MaxRetries` in code does. Setting a batch item's `Timeout` to null in
`configureEach` states "no timeout" and clears it.

**Settings nobody edits at runtime are unaffected.** The misfire options, exclusions and
variance are written at every start, with the scheduler-wide defaults where the options set
none. Moving one of them to the operator's side is a change to this ADR, not a local edit.

## Exemplars

- `SeedingPreservesOperatorStateTests` disables a manifest, edits its retries, timeout and
  priority, and edits a group at runtime, seeds again, and asserts the edits survive, and that a
  stated value still wins.
- `SeedingBuildValidationTests` pins the build failure for two members stating different group
  settings, and that agreeing or silent members build.
- [Scheduling Options](/docs/scheduler/scheduling-options#what-a-restart-rewrites) is the rule
  this produces.

Not covered: nothing checks that a newly added operator-editable setting is also made
state-only. A dashboard action that edits a new manifest or group field has to be paired with
the same treatment in `DataContextExtensions`, and that is caught in review.

## Changelog

- **2026-09-30**: A manifest's `MaxRetries`, timeout and priority are written on a re-seed only
  when the code states them, as its enabled flag is; they left "Settings nobody edits at runtime".

- **2026-09-30**: Recorded, with the change that made re-seeding write only stated settings.
