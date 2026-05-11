---
layout: default
title: PersistedOperationException
parent: Persisted Operations
grand_parent: SDK Reference
---

# PersistedOperationException

Abstract base for every structured failure raised by [IPersistedOperationStore.UpsertAsync](/docs/sdk-reference/persisted-operations/i-persisted-operation-store). Inherits `InvalidOperationException` so legacy callers that catch `InvalidOperationException` still see them.

All subclasses expose a stable `Code` string that matches the `code` field on the GraphQL mutation `errors[]` payload.

| Subclass | `Code` | When |
|---|---|---|
| `PersistedOperationParseException` | `PARSE_FAILED` | Document failed `Utf8GraphQLParser.Parse`. |
| `PersistedOperationValidationException` | `SCHEMA_VALIDATION_FAILED` | Document parsed but failed HotChocolate validation. |
| `ShapeDiffViolationException` | `SHAPE_DIFF_VIOLATION` | Edit changes the response shape of an existing id. |

## PersistedOperationParseException

| Property | Type | Notes |
|---|---|---|
| `Code` | `string` | Always `"PARSE_FAILED"`. |
| `Line` | `int?` | 1-based line number when the parser exposed one. |
| `Column` | `int?` | 1-based column number when the parser exposed one. |
| `OriginalMessage` | `string` | The parser's original message, preserved without prefixing. |

## PersistedOperationValidationException

| Property | Type | Notes |
|---|---|---|
| `Code` | `string` | Always `"SCHEMA_VALIDATION_FAILED"`. |
| `Failures` | `IReadOnlyList<ValidationFailure>` | One entry per HotChocolate validation error. Construction throws `ArgumentException` for an empty list (a validation exception with no failures is meaningless). |

`ValidationFailure` is a record with:

| Field | Type | Notes |
|---|---|---|
| `Message` | `string` | Validator-provided message. |
| `Locations` | `IReadOnlyList<ValidationFailureLocation>` | Empty when not available. |
| `Path` | `IReadOnlyList<object>` | Response-path components leading to the failure. Empty when not applicable. |

## ShapeDiffViolationException

| Property | Type | Notes |
|---|---|---|
| `Code` | `string` | Always `"SHAPE_DIFF_VIOLATION"`. |
| `Id` | `string` | The id whose shape would change. |
| `OldFingerprint` | `string` | Fingerprint stored on the existing row. |
| `NewFingerprint` | `string` | Fingerprint computed from the proposed new document. |

Pass `UpsertOptions { BypassShapeDiff = true }` (or `bypassShapeDiff: true` on the [GraphQL mutation](/docs/sdk-reference/persisted-operations/management-mutations)) when the change is verified safe for shipped clients.
