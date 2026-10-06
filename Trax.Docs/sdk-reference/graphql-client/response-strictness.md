---
layout: default
title: ResponseStrictness
description: Reference for ResponseStrictness, which decides how the client treats response fields that differ from the response type, and GraphQLResponseShapeException.
parent: GraphQL Client
grand_parent: SDK Reference
nav_order: 7
---

# ResponseStrictness

How the executor treats a response whose fields differ from the response type's properties. It catches the hand-written query and the POCO drifting apart: a property added to the type but not to the query, or a field selected but never read.

## Definition

```csharp
public enum ResponseStrictness
{
    Lenient,
    WarnOnDrift,
    ThrowOnDrift,
}
```

| Value | Behavior |
|-------|----------|
| `Lenient` (default) | No check. Extra JSON fields are ignored and missing ones leave the property at its default, as System.Text.Json does on its own |
| `WarnOnDrift` | Drift is logged at `Warning` under `ILogger<GraphQLClientExecutor>`, naming the type and the extra and missing fields. The call still succeeds |
| `ThrowOnDrift` | Drift throws `GraphQLResponseShapeException`. Recommended for development and integration tests |

Set it with [`WithStrictness`](/docs/sdk-reference/graphql-client/builder#withstrictness):

```csharp
services.AddTraxGraphQLClient(uri).WithStrictness(ResponseStrictness.ThrowOnDrift);
```

## What is compared

After the server answers, the element the request's `UnwrapDataElement` returns is compared with the public instance properties of the response type:

- **Extra**: a JSON field with no matching property.
- **Missing**: a property with no matching JSON field.

A property's expected name is its `[JsonPropertyName]` if it has one, otherwise its name passed through the JSON options' `PropertyNamingPolicy`, otherwise its name. Names are compared ignoring case, so `id` matches `Id`. A property marked `[JsonIgnore]` with `Condition = Always` is not expected; other `JsonIgnore` conditions affect writing only, so those properties still are.

The check:

- covers one level. Properties of nested objects are not compared.
- applies only when the unwrapped element is a JSON object. A scalar, a list or `null` is not checked.
- runs only for requests that use the default extractor (`UsesDefaultExtractor` is `true`). A request that overrides `Extract` is never checked.
- is cached per response type and set of JSON field names, so a response shape seen before costs a dictionary lookup.

## GraphQLResponseShapeException

```csharp
public class GraphQLResponseShapeException : Exception
{
    public Type TargetType { get; }
    public IReadOnlyList<string> ExtraJsonFields { get; }
    public IReadOnlyList<string> MissingJsonFields { get; }
}
```

Thrown by `IGraphQLClientExecutor.Run` under `ThrowOnDrift`. The message names the type and both field lists:

```
Response shape does not match PlayerProfile: fields declared on POCO not in response: level.
```

## Package

```
dotnet add package Trax.Api.GraphQL.Client
```
