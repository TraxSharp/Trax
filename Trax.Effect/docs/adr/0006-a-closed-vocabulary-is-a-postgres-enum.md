---
authors: [Theauxm]
areas: [data-model, migrations, providers]
status: accepted
---

# A closed vocabulary Trax persists is a Postgres enum

A column holding one of a fixed set of values Trax defines, such as a train state, a work queue
status or a failure class, is a Postgres enum type in the `trax` schema, mapped to the C# enum
with Npgsql's snake-case names. A new value is added with `ALTER TYPE ... ADD VALUE` in its own
migration, and every reader must know it before any writer uses it.

## Status

**Accepted.**

## Why this is written down

Because the rolling-deploy hazard of adding a value is easy to blame on the column type, and
that was nearly acted on. An old process cannot read a value its C# enum does not have, and that
is true whether the value arrives as an enum label or as text, because the failure is in mapping
it to the C# enum. Switching one vocabulary to text would buy nothing and make it the only one of
seven that is not an enum.

## Considered options

**Text with a check constraint, or a smallint.** Rejected. It does not avoid the reader problem
above, it loses the database-side guarantee that only known values are stored, and it would leave
the vocabularies split across two storage conventions.

## Consequences

**Adding a value is two releases.** First the migration and a build of every reader that knows
the value; then the code that writes it. Removing or renaming a value is not supported by
Postgres enums at all and needs a new type.

**The mapping lives in three places,** the Npgsql data source, the EF model and the EF options,
and all three must name the same enums. SQLite stores each enum as its integer value (EF's
default there), so raw SQL against SQLite, such as the dispatch claim and candidate queries, must
compare enum columns to those integers, not to the Postgres labels; the SQLite dialect generates
the literals from the enums for that reason. The same holds for SQLite migrations: 009 stores
`failure_class` as `INTEGER` defaulting to `0` (Unclassified), and 008's partial indexes filter on
the integer values. Because SQLite's SQL and indexes depend on those integers, the enums they
compare against pin their values explicitly (`TrainState` does, as do `WorkQueueStatus` and
`FailureClass`), so reordering members cannot silently change what a stored row means. The
in-memory provider stores the C# value and runs no SQL. Neither needs the Postgres mapping.

## Exemplars

- `PostgresEnumVocabularyTests` checks that the three mapping lists name the same enums, and that
  every mapped enum's members match the labels its migrations create and add.
- `SqliteFailureClassStorageTests` pins each `FailureClass` member's integer, and checks that
  SQLite stores the integer rather than the label.

Not covered: the guard proves the vocabularies agree, not that a new value's migration shipped
before the code that writes it. That ordering is a release decision no test in one repo can see.

## Changelog

- **2026-09-23**: Recorded.
