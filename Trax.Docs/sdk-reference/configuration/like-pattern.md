---
title: LikePattern
description: Reference for LikePattern, which turns a caller's search term into an escaped, lowered LIKE pattern that the Postgres trigram indexes serve.
type: reference
area: data
parent: Configuration
grand_parent: SDK Reference
nav_order: 44
package: Trax.Effect.Data
namespace: Trax.Effect.Data.Utils
source:
  - Trax.Effect/src/Trax.Effect.Data/Utils/LikePattern.cs
---

# LikePattern

Builds the pattern for a case-insensitive search for text inside a column. Trax's own text filters
use it: the scheduler's log filters (`MessageContains`, `CategoryContains`) and the GraphQL
`executions` query's `failureReasonContains`.

## Signature

```csharp fragment
namespace Trax.Effect.Data.Utils;

public static class LikePattern
{
    public const string Escape = "\\";
    public static string Contains(string term);
}
```

## Members

| Member | Description |
|--------|-------------|
| `Escape` | The escape character the pattern uses, a backslash, to pass to `EF.Functions.Like` |
| `Contains(term)` | The pattern for text containing `term`, below |

## Behavior

Returns `%` + the term, lowered, + `%`, with `%`, `_` and the escape character `\` in the term escaped,
so each matches only itself. A search for `50%` finds the text `50%`, not every value containing `50`,
and a term of only `%` does not match everything. A `null` term throws `ArgumentNullException`.

| Term | Pattern |
|------|---------|
| `TimeOut` | `%timeout%` |
| `50%` | `%50\%%` |
| `tenant_limit` | `%tenant\_limit%` |

The match is case-insensitive when the column is lowered too, which every provider translates. On Postgres that becomes `lower(failure_reason) LIKE @pattern ESCAPE '\'`. The trigram indexes Trax
ships (`ix_log_message_trgm` and `ix_log_category_trgm` from migration
[063](/docs/reference/schema-migrations#log-text-search-063), and
`ix_metadata_failure_reason_trgm` from [066](/docs/reference/schema-migrations#failure-search-066))
are built over the lowered column, so a search written another way, with `ILIKE` or without
`lower`, reads the whole table. A term shorter than three characters has no trigram to look up and
reads the table either way.

## Example

```csharp compile
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Utils;
using Trax.Effect.Models.Metadata;

public class FailureSearch(IDataContext db)
{
    public IQueryable<Metadata> Matching(string term)
    {
        var pattern = LikePattern.Contains(term);
        return db.Metadatas.Where(m =>
            EF.Functions.Like(m.FailureReason!.ToLower(), pattern, LikePattern.Escape));
    }
}
```

## See also

- [IDataContext](/docs/sdk-reference/configuration/i-data-context)
