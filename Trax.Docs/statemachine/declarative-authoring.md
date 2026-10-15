---
title: Write guards and reducers as data
description: "Author a state machine's guards, reducers and context rules as data from records and the Rules vocabulary, so the machine exports its IR and can be served."
type: how-to
area: machines
nav_order: 2
source:
  - Trax.Effect/src/Trax.Effect.StateMachine/Rules
---

# Write guards and reducers as data

A guard written as `When(Func...)`, a reducer as `Reduce(Func...)` and a state rule as `Holds(...)` run, but they
are closures nothing can read. Written as data instead (a `Rule`, a `Reduction`, a schema reflected from a record),
the machine behaves the same and exports an [IR](/docs/sdk-reference/statemachine-api/ir-format), which is what
serving it to clients and generating its TypeScript twin both need. Why the IR matters is in
[The codegen pipeline](/docs/statemachine/codegen-pipeline).

## 1. Describe each context and input as a record

Give every state that carries data a context record, and every trigger that takes input an input record. The
schema (field names, JSON types, nullability, constraints) comes from the type, and attributes add constraints:
`[MinLength(1)]` is non-empty, `[MaxLength(n)]` bounds a length or a count.

```csharp compile=turnstile
using System.ComponentModel.DataAnnotations;

public sealed record UnlockedContext
{
    [MinLength(1)]
    public string PaidWith { get; init; } = "";
}

public sealed record CoinInput
{
    public string Coin { get; init; } = "";
}
```

[Context and input schemas](/docs/statemachine/context-schemas) lists the JSON type each CLR type maps to and the
property types that refuse to configure.

## 2. Declare states and edges with rules

`Context<T>()` declares a state's shape from its record, and `Context()` a state that carries none.
`WithInput<T>()` declares a trigger's input. `When(Rule)` and `Reduce(Reduction)` take the data forms built by the
[Rules vocabulary](/docs/sdk-reference/statemachine-api/rules), and fields are referenced by member expression
(`i => i.Coin`), never by string.

```csharp compile=turnstile
using System.Text.Json.Nodes;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;
using static Trax.Effect.StateMachine.Rules;

public sealed class TurnstileMachine : Machine<TurnstileState, TurnstileTrigger>
{
    protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m)
    {
        m.Id("turnstile").Version(1).StartsAt(TurnstileState.Locked, () => new JsonObject());

        m.In(TurnstileState.Locked)
            .Context()
            .On(TurnstileTrigger.Coin)
            .WithInput<CoinInput>()
            .When(Input((CoinInput i) => i.Coin).IsOneOf("quarter", "dollar"))
            .Because("Only a quarter or a dollar is accepted.")
            .Reduce(Set((UnlockedContext u) => u.PaidWith).FromInput((CoinInput i) => i.Coin))
            .To(TurnstileState.Unlocked);

        m.In(TurnstileState.Unlocked)
            .Context<UnlockedContext>()
            .On(TurnstileTrigger.Push)
            .Reduce(Clear())
            .To(TurnstileState.Locked);
    }
}
```

`TurnstileState` (`Locked`, `Unlocked`) and `TurnstileTrigger` (`Coin`, `Push`) are plain enums. The guard on
`Locked -Coin-> Unlocked`, the reduction that records `paidWith` and the `Unlocked` context rule are now data, which
both engines evaluate through the same interpreter.

## 3. Name what the vocabulary cannot express

For a guard or reduction the vocabulary has no form for (arithmetic, a lookup), use a custom one rather than a
delegate: `new Rule.Custom(name)` with `CustomGuard(name, handler)`, or `new Reduction.Custom(name)` with
`CustomReducer(name, handler)`, on the machine builder. The IR carries the name, and each runtime binds a handler
for it (`customGuards` and `customReducers` in TypeScript). `Build()` throws for a name with no handler. A generated
twin binds none: it reads a snapshot the server sent as passing the rule, and does not predict a transition that
needs it, so the server's answer is what a client shows. See
[Beyond the vocabulary](/docs/statemachine/delegates-and-declarative-rules#why-the-vocabulary-is-small).

## 4. Check that the machine exports

A machine may mix delegate and data forms while you migrate it edge by edge, because both live on one builder. The
export does not accept the mix: once a machine makes any declarative call, `ExportIr()` throws
`InvalidOperationException` while any edge still has a delegate guard or reducer, or any state a `Holds` validator,
and the message names each one. Otherwise the generated twin would treat such an edge as unconditional and keep the
context while the server runs the delegate. Run [`trax machine generate`](/docs/reference/cli#trax-machine-generate)
or [`trax machine check`](/docs/reference/cli#trax-machine-check) to export it. See
[Delegate vs declarative](/docs/statemachine/delegates-and-declarative-rules) and
[Exporting the IR](/docs/sdk-reference/statemachine-api/machine#exporting-the-ir).

To serve the machine to clients, add the caller rules and the `[TraxMachine]` mark described in
[Author and serve a machine](/docs/statemachine/authoring).

## SDK Reference

> [Context&lt;T&gt;](/docs/sdk-reference/statemachine-api/machine#istatebuilder) | [WithInput&lt;T&gt;](/docs/sdk-reference/statemachine-api/machine#trigger-input) | [When(Rule) / Reduce(Reduction)](/docs/sdk-reference/statemachine-api/machine) | [Rules vocabulary](/docs/sdk-reference/statemachine-api/rules) | [Declarative data model](/docs/sdk-reference/statemachine-api/declarative-data-model)
