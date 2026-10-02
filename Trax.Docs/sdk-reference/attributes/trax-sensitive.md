---
layout: default
title: TraxSensitive
description: Reference for TraxSensitive, which masks a member of a train's input or output in every stored copy, what is masked, the rules and what it does not cover.
parent: Attributes
grand_parent: SDK Reference
nav_order: 3
---

# TraxSensitive

Marks a property, field or record parameter of a train's input or output, or of anything reachable from them, whose value must not appear in the copies Trax keeps. The train runs with the real value; only the written copy is masked.

## Signature

```csharp
namespace Trax.Effect.Attributes;

[AttributeUsage(
    AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter,
    AllowMultiple = false,
    Inherited = true
)]
public sealed class TraxSensitiveAttribute : Attribute
{
    public TraxSensitiveAttribute();
}
```

## What is masked

A marked value is written as `{"_redacted": true}` under the same JSON name in:

| Copy | Written by |
|------|------------|
| The stored input and output of a run | [SaveTrainParameters](/docs/sdk-reference/configuration/save-train-parameters) |
| A junction's logged output | [AddJunctionLogger](/docs/sdk-reference/configuration/add-junction-logger) |
| The output handed to lifecycle hooks | [AddLifecycleHook](/docs/sdk-reference/configuration/add-lifecycle-hook), the broadcaster, GraphQL subscriptions |

and so everywhere those copies travel: the dashboard, the API's execution detail, logs and subscriptions.

## Rules

- **Opt-in.** Nothing is masked because of its name.
- **A marked member hides its whole value.** An object under it is not walked; a collection is replaced whole.
- **An unmarked member is walked.** A marked property on a nested object, or on each element of a collection, is masked where it sits.
- **Records.** On a positional record, write the attribute on the parameter, with or without `property:`.
- **Inheritance.** A mark on a base property, or on an interface property, applies to the override or implementation.
- **Dictionaries are not masked.** Keys and values have no member to mark.

## What it does not cover

The copy a train is *run* from keeps the real value, because the train needs it: a queued entry's input (`work_queue.input`) and a manifest's properties. Those are JSON strings the mark cannot reach into, so Trax keeps them out of its logs instead: a model's `ToString()`, the JSON effect and the junction logger write each as `{"_omitted": true}`. They are still readable wherever those columns are. Keep a secret out of an input where you can, and pass a reference to it instead.

## Example

```csharp
using Trax.Effect.Attributes;

public record ConnectAccountInput(
    string AccountId,
    [property: TraxSensitive] string ApiToken
) : IManifestProperties;
```

The run's stored input holds `{"_redacted": true}` where the token would be; the junctions receive the token.

## Package

```
dotnet add package Trax.Effect
```
