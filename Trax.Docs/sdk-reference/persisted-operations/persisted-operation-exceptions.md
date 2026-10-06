---
layout: default
title: PersistedOperationException
description: Reference for PersistedOperationException and its parse, validation, shape-diff and input subclasses, each with a stable error code.
parent: Persisted Operations
grand_parent: SDK Reference
---

# PersistedOperationException

Abstract base for every structured failure raised by [IPersistedOperationStore](/docs/sdk-reference/persisted-operations/i-persisted-operation-store): a refused upload, or a saved change that did not reach every node. Inherits `InvalidOperationException` so legacy callers that catch `InvalidOperationException` still see them.

All subclasses expose a stable `Code` string that matches the `code` field on the GraphQL mutation `errors[]` payload.

| Subclass | `Code` | When |
|---|---|---|
| `PersistedOperationParseException` | `PARSE_FAILED` | Document failed `Utf8GraphQLParser.Parse`. |
| `PersistedOperationValidationException` | `SCHEMA_VALIDATION_FAILED` | Document parsed but failed HotChocolate validation. |
| `ShapeDiffViolationException` | `SHAPE_DIFF_VIOLATION` | Edit changes the response shape of an existing id. |
| `PersistedOperationInputException` | `INVALID_INPUT` | Document holds no operation, or more than one. A persisted document holds exactly one. |
| `PersistedOperationNotBroadcastException` | `CHANGE_NOT_BROADCAST` | An upload, deactivation or restore was saved and applied on this node, but the broker did not confirm its broadcast. |

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

Pass `UpsertOptions { BypassShapeDiff = true }` (or `bypassShapeDiff: true` on the [GraphQL mutation](/docs/sdk-reference/persisted-operations/management-mutations)) when the change is verified safe for shipped clients. The exception's message names the C# option; the mutation's `SHAPE_DIFF_VIOLATION` error names `bypassShapeDiff: true`, the lever a GraphQL caller has.

## PersistedOperationInputException

| Property | Type | Notes |
|---|---|---|
| `Code` | `string` | Always `"INVALID_INPUT"`. |

Thrown after validation, for every validator including the no-op one `AddPersistedOperationStore` registers.

## PersistedOperationNotBroadcastException

| Property | Type | Notes |
|---|---|---|
| `Code` | `string` | Always `"CHANGE_NOT_BROADCAST"`. |
| `Id` | `string` | The id of the operation that changed. |
| `Operation` | `PersistedOperation?` | The row as saved, for an upload; null for a deactivation or a restore. |
| `InnerException` | `Exception` | The broadcaster's failure: a refused or unanswered publisher confirm (ten seconds), or no connection to the broker. |

Unlike the other subclasses, the change it reports is committed. The node that made it has already emptied its caches; every other node keeps serving what it cached until that entry reaches its maximum age (`WithCacheMaxAge`), then reads the store again. Repeating the change (re-upload the same document, deactivate again) sends it again. The [management mutations](/docs/sdk-reference/persisted-operations/management-mutations#error-payload) return it as a payload error beside the saved operation.

## Request errors

Two refusals happen when a request is executed, not when an operation is stored, so they are GraphQL errors in the response's top-level `errors` rather than exceptions. Both answer HTTP 400.

| `extensions.code` | When |
|---|---|
| `PERSISTED_OPERATION_REQUIRED` | Enforcement refuses an inline document. See [UsePersistedOperationsEnforcement](/docs/sdk-reference/persisted-operations/use-persisted-operations-enforcement). |
| `PERSISTED_OPERATION_ID_MISMATCH` | The request sends a persisted operation id and a document, and the id is not the document's own hash under the executor's hash algorithm. Send the id alone to run the stored operation, or the document alone to run it inline. See [Persisted Operations](/docs/persisted-operations). |

Every code the endpoint returns is listed in [Error Codes](/docs/sdk-reference/graphql-api/error-codes).
