---
layout: default
title: ShapeFingerprintComputer
parent: Persisted Operations
grand_parent: SDK Reference
---

# ShapeFingerprintComputer

Computes a canonicalized structural hash (sha-256, hex) of the response shape produced by a GraphQL operation. Two documents that produce byte-compatible JSON shapes hash to the same fingerprint; documents that would change a shipped client's deserialization hash differently.

The storage layer calls this on every `UpsertAsync` and stores the result in `shape_fingerprint`. The shape-diff guardrail (v1.1 dashboard) compares old vs new on edits.

## Signature

```csharp
public static string Compute(string document, string? operationName = null);
```

| Parameter | Notes |
|---|---|
| `document` | Full GraphQL document text. Required. |
| `operationName` | Disambiguates when the document contains multiple operations. When null and the document has exactly one operation, that one is used. |

Throws `InvalidOperationException` when the document has no executable operation, when multiple operations are present without a name, or when the named operation is missing.

## Algorithm

1. Parse the document via `Utf8GraphQLParser`.
2. Locate the executable operation.
3. Inline fragment spreads with their type conditions. Cycles are broken by a recursion guard.
4. For every field emit `(parentType, responseKey, fieldName, hasInclude, hasSkip)` where `responseKey` is the alias when present, else the field name.
5. Sort sibling tuples by `(parentType, responseKey, fieldName)`.
6. Recurse into nested selections, indented by depth.
7. Hash the canonical string with sha-256.

## What is "same shape"

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

## What is "different shape"

| Different fingerprint | Why |
|---|---|
| Adding / removing / renaming a selected field | Response keys differ. |
| Adding or changing an alias | Response key differs even if underlying field is the same. |
| Fragment-spread vs inlined fields | Conservative: fragment carries a type condition that may matter on unions/interfaces. Operators rewriting can pass `--force` if they have verified the type is concrete. |
| Inline-fragment branch added or removed (e.g. on a union) | Real shape change. |
| Adding `@include` or `@skip` to a field | Field becomes conditional. |
| Mutation vs query, subscription vs query | Operation type is part of the canonical form. |

## Conservatism

The fingerprint errs on the side of false positives. The dashboard's `--force` path (v1.1) is the documented escape hatch for cases where the operator has verified a flagged change is shape-safe. False negatives (silently approving a real shape change) would be the dangerous direction; the algorithm is biased away from them.

A binding test fixture lives in `Trax.Api.Tests.PersistedOperations.UnitTests.ShapeFingerprintComputerTests` with ~50 input pairs labeled "same shape" or "different shape", covering fragments, unions, interfaces, directives, aliases, variables, deep nesting, whitespace, and field-order canonicalization. New canonicalization rules MUST add new pairs there.
