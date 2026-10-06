---
layout: default
title: LikePattern
description: Reference for LikePattern, which turns a caller's search term into an escaped, lowered LIKE pattern that the Postgres trigram indexes serve.
parent: Configuration
grand_parent: SDK Reference
nav_order: 25
---

# LikePattern

Builds the pattern for a case-insensitive search for text inside a column. Trax's own text filters
use it: the scheduler's log filters (`MessageContains`, `CategoryContains`) and the GraphQL
`executions` query's `failureReasonContains`. Use it in your own queries over the `trax` tables so a
search reads the same way and reaches the same indexes.

## Signature

```csharp
namespace Trax.Effect.Data.Utils;

public static class LikePattern
{
    public const string Escape = "\\";
    public static string Contains(string term);
}
```

## Contains

Returns `%` + the term, lowered, + `%`, with `%`, `_` and the escape character `\` in the term escaped,
so each matches only itself. A search for `50%` finds the text `50%`, not every value containing `50`,
and a term of only `%` does not match everything. A `null` term throws `ArgumentNullException`.

| Term | Pattern |
|------|---------|
| `TimeOut` | `%timeout%` |
| `50%` | `%50\%%` |
| `tenant_limit` | `%tenant\_limit%` |

Pass the pattern with `Escape` and lower the column, which is the case-insensitive match every
provider translates:

```csharp
var pattern = LikePattern.Contains(term);
var runs = db.Metadatas.Where(m =>
    EF.Functions.Like(m.FailureReason!.ToLower(), pattern, LikePattern.Escape)
);
```

On Postgres that becomes `lower(failure_reason) LIKE @pattern ESCAPE '\'`. The trigram indexes Trax
ships (`ix_log_message_trgm` and `ix_log_category_trgm` from migration
[063](/docs/migration-guides/database-migrations#log-text-search-063), and
`ix_metadata_failure_reason_trgm` from [066](/docs/migration-guides/database-migrations#failure-search-066))
are built over the lowered column, so a search written another way, with `ILIKE` or without
`lower`, reads the whole table. A term shorter than three characters has no trigram to look up and
reads the table either way.
