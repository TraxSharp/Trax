---
title: ShapeFingerprintComputer
description: "Reference for ShapeFingerprintComputer, the response-shape hash the store compares to refuse persisted-operation edits that change a response's shape."
type: reference
area: api
parent: Persisted Operations
grand_parent: SDK Reference
nav_order: 12
package: Trax.Api.GraphQL.PersistedOperations
namespace: Trax.Api.GraphQL.PersistedOperations.ShapeDiff
source:
  - Trax.Api/src/Trax.Api.GraphQL.PersistedOperations/ShapeDiff/ShapeFingerprintComputer.cs
---

# ShapeFingerprintComputer

Computes a canonicalized structural hash (sha-256, hex) of the response shape produced by a GraphQL operation. Two documents that produce byte-compatible JSON shapes hash to the same fingerprint; documents that would change a shipped client's deserialization hash differently.

The storage layer calls this on every `UpsertAsync` and stores the result in `shape_fingerprint`. The shape-diff guardrail in the store compares old vs new on every edit of an existing id and refuses a change unless `UpsertOptions.BypassShapeDiff` is set.

## Signature

```csharp fragment
public static string Compute(string document, string? operationName = null);
```

## Parameters

| Parameter | Notes |
|---|---|
| `document` | Full GraphQL document text. Required. |
| `operationName` | Disambiguates when the document contains multiple operations. When null and the document has exactly one operation, that one is used. |

## Returns

The sha-256 hash of the canonical shape, as lowercase hex.

## Behavior

Throws `InvalidOperationException` when the document has no executable operation, when multiple operations are present without a name, or when the named operation is missing.

### Algorithm

1. Parse the document via `Utf8GraphQLParser`.
2. Locate the executable operation.
3. Inline fragment spreads with their type conditions. Cycles are broken by a recursion guard.
4. For every field emit `(parentType, responseKey, fieldName, hasInclude, hasSkip)` where `responseKey` is the alias when present, else the field name.
5. Sort sibling tuples by `(parentType, responseKey, fieldName)`.
6. Recurse into nested selections, indented by depth.
7. Hash the canonical string with sha-256.

### What is "same shape"

| Same fingerprint | Why |
|---|---|
| Whitespace, indentation, comments | Lexed away. |
| Field order within a selection set | Canonical sort. |
| Argument value changes | Arguments do not affect response shape. |
| Argument additions / removals | Same. |
| Variable type widening (`Int!` -> `ID!`) | Variables are inputs. |
| Adding a new optional variable | Old clients ignore it. |
| Default variable value changes | Same. |
| Pure-resolver-path swaps with identical projection | Same response keys. |

### What is "different shape"

| Different fingerprint | Why |
|---|---|
| Adding / removing / renaming a selected field | Response keys differ. |
| Adding or changing an alias | Response key differs even if underlying field is the same. |
| Fragment-spread vs inlined fields | Conservative: fragment carries a type condition that may matter on unions/interfaces. Operators rewriting can set `BypassShapeDiff` if they have verified the type is concrete. |
| Inline-fragment branch added or removed (e.g. on a union) | Real shape change. |
| Adding `@include` or `@skip` to a field | Field becomes conditional. |
| Mutation vs query, subscription vs query | Operation type is part of the canonical form. |

### False positives

The fingerprint is conservative: a change it flags may still be shape-safe for a given client, and
`BypassShapeDiff` (the upload mutation's `bypassShapeDiff`, the dashboard editor's bypass checkbox)
stores it anyway. It does not pass a change that alters the response keys. The cases are pinned by
`ShapeFingerprintComputerTests` in `Trax.Api.Tests.PersistedOperations.UnitTests`.

## Example

```csharp compile
var before = ShapeFingerprintComputer.Compute("query U { user(id: 1) { id name } }");
var after = ShapeFingerprintComputer.Compute("query U { user(id: 2) { name id } }");

Console.WriteLine(before == after); // True: argument values and field order do not change the shape
```

## See also

- [IPersistedOperationStore](/docs/sdk-reference/persisted-operations/i-persisted-operation-store)
- [PersistedOperationException](/docs/sdk-reference/persisted-operations/persisted-operation-exceptions#shapediffviolationexception)
