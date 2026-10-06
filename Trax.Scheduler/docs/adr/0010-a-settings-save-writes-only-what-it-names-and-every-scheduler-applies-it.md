---
authors: [Theauxm]
areas: [scheduling]
status: accepted
---

# A settings save writes only what it names, and every running scheduler applies it

`OperationsService.UpdateSchedulerConfigAsync` records in the `trax.scheduler_config` row only
the settings a patch sets, in the row's `overrides` object, and every scheduler host re-reads that
row every few seconds (`SchedulerConfiguration.SettingsRefreshInterval`, 5 seconds) and applies it
without a restart. A setting the row names replaces the code value on every host; every other
setting keeps each host's code value. The settings are listed once, in `SchedulerSettings`, and
the save, the startup load and the refresh all go through that list.

## Status

**Accepted.** In force since Trax.Effect 1.57.4 added `scheduler_config.overrides`.

## Why

The save used to copy the saving process's whole configuration into the row. The operations
service also runs on API-only hosts (`AddTraxJobRunner()` plus `OperationsService`), whose
configuration is the empty one `AddTraxJobRunner` registers, so changing one setting there
stored defaults for all the others, and the scheduler took them at its next restart. Between two
scheduler hosts a save carried the saving host's values for every field, and reached the other
host only when it restarted. Once any save had happened, a later change to any setting in code
was silently ignored, because the row won.

The options were to refuse saves on a host that does not run the scheduler and document that a
save pins every setting, or to make a save sparse and have schedulers pick it up while running.
The second is what an operator means by changing one setting, and it is the one the dashboard and
the GraphQL API already describe, so it is the one chosen.

The row keeps its typed columns and gains one `overrides` column (`jsonb` on Postgres, `TEXT` on
SQLite) holding a JSON object of setting name to value, rather than making every column nullable.
A new setting then needs no migration (`FailureCountWindow` has no column and is stored only
there), and a host still on a version that reads the columns keeps working through a rolling
deploy: the save keeps a named setting's column in step, and a scheduler host fills the other
columns with the values it runs with when it creates the row.

A poll rather than the `SchedulerConfig` change signal carries the save to other processes: the
signal crosses processes only when the host has a broadcaster configured, and a settings change
must arrive either way. One primary-key read every few seconds per scheduler host is cheap.

## Consequences

**Any host may make the first save.** Creating the row names only what the save sets, so an
API-only host no longer needs the values the scheduler runs with, and is no longer refused.

**A change in code applies to every setting no save named.** A setting the row names keeps its
stored value over the code value until the row changes; deleting the row returns every running
scheduler to its configured values within one refresh.

**What counts as a change depends on the host.** A setting the row names changes when the patch
differs from the stored value. For one it does not name, a scheduler host compares with the value
it runs with, so a dashboard form that sends every field names only the ones the operator edited.
Any other host cannot know that value, so every field its patch sets is stored: a patch there
should name only what it means to change, which the GraphQL `updateScheduler` mutation does.

**A row written before `overrides` existed keeps every value.** Its `overrides` is null, and it is
read as setting every setting it has a column for, as every save used to write them all. The
first save to it names each of those in `overrides` before applying the patch, so no operator's
saved value is dropped on upgrade.

**The dead-letter purge fails closed.** `AutoPurgeDeadLetters` and `DeadLetterRetentionPeriod`
delete data, so for them a saved value does not simply win. When the builder states one in code
and the row names one too, the purge runs only if both allow it, and the longer retention applies.
A saved value may make the purge more cautious than the code, never less. The alternative, letting
the row win as it does for every other setting, meant that a deploy adding
`.AutoPurgeDeadLetters(false)` to keep dead letters for an audit had no effect on a host where an
operator had once saved any setting, and nothing said so. Whenever a saved value replaces a
different code value, for any setting, the scheduler logs a warning when it applies the row.

**`LocalWorkerCount` applies at the next start of the worker pool.** It is stored and applied to
the options at once, but the pool starts its workers once.

## Exemplars

- `SchedulerConfigFromApiHostTests` asserts that a change from an API host leaves the scheduler's
  other settings alone, that an API host may make the first save, that a later change in code
  applies to a setting no save named, that a row written before `overrides` keeps its values
  through a save, that a change saved on one host (including the failure count window, which has
  no column) reaches a running scheduler on another, and that removing the row restores the
  configured values; and that a saved purge does not turn on a purge turned off in code, and the
  longer of a saved and a coded retention applies.
- [Mutations: config](/docs/sdk-reference/graphql-api/mutations) is the rule this produces.

Not covered: nothing asserts that a new setting is added to `SchedulerSettings.All`; one left out
is neither saved nor applied, and would have to be caught in review. Nothing yet removes a single
setting from `overrides` to return it to its code value; deleting the row returns all of them.

## Changelog

- **2026-09-30**: Recorded.
- **2026-09-30**: The sparse half is in force on Trax.Effect 1.57.4's `overrides`: the first-save
  refusal is gone, `FailureCountWindow` is stored, and a code change applies to every setting no
  save named.
- **2026-09-30**: The dead-letter purge settings fail closed when code and the row both state
  them, and a saved value replacing a different code value is logged.
