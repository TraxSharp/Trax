---
title: TrainInputReader
description: "Reference for TrainInputReader: Read, Write and ResolveSavedInput, the rules every Trax surface reads and writes train input JSON by."
type: reference
area: mediator
parent: Mediator API
grand_parent: SDK Reference
nav_order: 4
package: Trax.Mediator
namespace: Trax.Mediator.Services.TrainExecution
source:
  - Trax.Mediator/src/Trax.Mediator/Services/TrainExecution/TrainInputReader.cs
---

# TrainInputReader

The rules for reading a caller's train input JSON, as a public static class.
[ITrainExecutionService](/docs/sdk-reference/mediator-api/i-train-execution-service)'s
`QueueAsync`, `RunAsync` and `PrepareAsync` read input through it, and a host or package that takes
train input JSON on a path of its own calls it to accept and refuse the same JSON. One that builds
an input object and hands it to `ITrainExecutionService` as JSON, as the GraphQL train mutations
do, writes it with `Write`.

## Signature

```csharp fragment
namespace Trax.Mediator.Services.TrainExecution;

public static class TrainInputReader
{
    public const int StoredInputGrowthFactor = 4;

    public static object Read(
        string? inputJson,
        TrainRegistration registration,
        int maxInputJsonBytes
    );

    public static string ResolveSavedInput(
        string savedInputJson,
        TrainRegistration registration,
        int maxInputJsonBytes
    );

    public static string Write(object? input, Type inputType);
}
```

## Members

| Member | Description |
|--------|-------------|
| `StoredInputGrowthFactor` | How many times `MaxInputJsonBytes` a queued input's stored form may be: 4 |
| [`Read`](#read) | Reads a caller's JSON into the train's input type |
| [`Write`](#write) | Writes an input as JSON that `Read` reads back as the same input |
| [`ResolveSavedInput`](#resolvesavedinput) | Turns a run's saved input into JSON that `Read` accepts |

## Read

| Parameter | Type | Description |
|-----------|------|-------------|
| `inputJson` | `string?` | The caller's JSON. Null or blank is read as `{}` |
| `registration` | `TrainRegistration` | The train whose `InputType` is read |
| `maxInputJsonBytes` | `int` | The size cap in UTF-8 bytes, normally `MediatorConfiguration.MaxInputJsonBytes` |

**Returns**: an instance of `registration.InputType`, never null.

**Throws**: `TrainInputValidationException` when the JSON is larger than `maxInputJsonBytes`,
checked before parsing. `JsonException` when it cannot be read as the input type, names a property
twice in the same or another casing, uses `$id`/`$values` reference metadata where the type
expects a list, is the literal `null`, or is blank for an input type that needs values (a
constructor parameter with no default, or a `required` member). When JSON whose root carries an
`$id` is refused, the message also says that reference metadata is not accepted and names `Write`
and `ResolveSavedInput`.

Property names match whatever their case, so `{"Amount":5}` and `{"amount":5}` are the same input
(`Trax.Docs/adr/0023`). `$ref` is read as an unknown property, not as a reference. `Read` does not
authorize; `PrepareAsync` calls it after the caller has been authorized for the train, so a caller
who may not use the train learns nothing about its input from a parse error.

## Write

| Parameter | Type | Description |
|-----------|------|-------------|
| `input` | `object?` | The input, an instance of `inputType` |
| `inputType` | `Type` | The train's input type |

**Returns**: the input as JSON that `Read` reads back as the same input.

It writes with the options `Read` reads with: the host's
[`SaveTrainParameters`](/docs/sdk-reference/configuration/save-train-parameters) options, so their
naming policy and converters apply, without reference handling. Those options preserve references
(`TraxJsonSerializationOptions.Default` does, unless the host gives others), and a list written
with them is an object of `$values` that `Read` refuses. Up to Trax.Api.GraphQL 1.61.0 the GraphQL
train mutations wrote their input with the host's options directly, so from Trax.Mediator 1.23.2,
which stopped honouring reference metadata in a caller's input, any run, queue or prepare whose
input held a list or an array failed before the train ran.

## ResolveSavedInput

Turns a run's saved input (the `Input` column
[`SaveTrainParameters()`](/docs/sdk-reference/configuration/save-train-parameters) writes) into JSON
that `Read` reads as the input the run was given. A re-queue passes the saved input through it
first.

| Parameter | Type | Description |
|-----------|------|-------------|
| `savedInputJson` | `string` | The saved input |
| `registration` | `TrainRegistration` | The train the input was saved for |
| `maxInputJsonBytes` | `int` | The cap `Read` will hold the result to, normally `MediatorConfiguration.MaxInputJsonBytes` |

**Returns**: the input as a plain JSON tree; an input with no reference metadata at its root,
written back compact; input that is not JSON, unchanged, for `Read` to refuse.

**Throws**: `JsonException` when the saved input's reference metadata is malformed or sits where a
reference-preserving writer would not have put it, one `$id` is given to two values, a `$ref` names
an `$id` that is not there, a value refers to a value that contains it (a cycle has no plain tree),
or the resolved input nests more than 64 levels deep. `TrainInputValidationException` when the
plain form is larger than `maxInputJsonBytes`. Writing stops the moment the result passes the cap,
so references that copy one value many times over never make a small saved input into a large one.

`SaveTrainParameters()` writes with `TraxJsonSerializationOptions.Default`, which preserves
references: every object carries an `$id`, a list is written as `{"$id":"2","$values":[...]}`, and
a second occurrence of the same object as `{"$ref":"3"}`. `ResolveSavedInput` rewrites a saved
input whose root object carries an `$id` as the plain tree it stands for: each `$id` dropped, each
`$values` object written as its array, and each `$ref` written as a full copy of the value it names,
so no two members of the input share an object. It recognises the metadata wherever it sits in its
object, because a `jsonb` column orders an object's properties shortest name first, so an input
with a one- or two-letter property such as `id` comes back as `{"id": 5, "$id": "1", ...}`. Before
Trax.Mediator 1.25.0 only an `$id` in first place was recognised.

Any other input keeps every member as it is, reference metadata below the root included, and is
written back compact: a saved input read from a `jsonb` column comes back with a space after every
colon and comma, which would put one saved at `MaxInputJsonBytes` over it. Before Trax.Mediator
1.25.0 such an input was returned unchanged. Compacting removes only that growth: the saved form
carries every member, so an input that left members out and was accepted near the cap can still be
refused when it is re-queued.

Nothing it does depends on the input type, so it reveals nothing about the input and may run
before the caller is authorized. Its result still goes through `Read`, and through authorization,
like any other input.

## Example

```csharp compile
public static class OrderInputs
{
    public static OrderInput ReadOrder(
        string inputJson,
        TrainRegistration registration,
        MediatorConfiguration configuration) =>
        (OrderInput)TrainInputReader.Read(inputJson, registration, configuration.MaxInputJsonBytes);

    public static string WriteOrder(OrderInput input) =>
        TrainInputReader.Write(input, typeof(OrderInput));
}
```

## See also

- [ITrainExecutionService](/docs/sdk-reference/mediator-api/i-train-execution-service)
- [SaveTrainParameters](/docs/sdk-reference/configuration/save-train-parameters)
- [Mediator exceptions](/docs/sdk-reference/mediator-api/exceptions)
