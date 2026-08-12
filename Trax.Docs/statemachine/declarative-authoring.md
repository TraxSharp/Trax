---
layout: default
title: Declarative authoring
parent: State Machines
nav_order: 2
---

# Declarative authoring

[Authoring a machine](/docs/statemachine/authoring) writes guards, reducers, and validators as C# delegates.
They run, but they are opaque closures: the engine can execute them, and nothing more. Declarative authoring
writes the same three things as data. A guard becomes a `Rule`, a reducer becomes a `Reduction`, and a state's
context rule becomes a schema reflected from a record. The machine then behaves identically (the data compiles
to the same engine delegates) and, because the behaviour is now inspectable, it exports a neutral IR that
generates the frontend twin instead of you hand-writing it.

## Context and input are records

The state's context and the trigger's input are C# records. The schema, its field names, JSON types,
nullability, and constraints, falls out of the type. `[MinLength(1)]` on a string or array becomes a
non-empty constraint.

```csharp
using System.ComponentModel.DataAnnotations;
using static Trax.Effect.StateMachine.Rules;

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

## The machine

`Context<T>()` declares a state's shape from its record. `WithInput<T>()` declares a trigger's input shape.
`When(Rule)` and `Reduce(Reduction)` take the data forms built by the [Rules vocabulary](/docs/sdk-reference/statemachine-api/rules).
Fields are referenced by member expression (`i => i.Coin`), never by string.

```csharp
public sealed class TurnstileMachine : Machine<TurnstileState, TurnstileTrigger>
{
    protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m)
    {
        m.Id("turnstile").Version(1).StartsAt(TurnstileState.Locked, () => new JsonObject());

        m.In(TurnstileState.Locked)
            .Context()                                              // Locked carries no context
            .On(TurnstileTrigger.Coin)
                .WithInput<CoinInput>()
                .When(Input((CoinInput i) => i.Coin).IsOneOf("quarter", "dollar"))
                .Because("Only a quarter or a dollar is accepted.")
                .Reduce(Set((UnlockedContext u) => u.PaidWith).FromInput((CoinInput i) => i.Coin))
                .To(TurnstileState.Unlocked);

        m.In(TurnstileState.Unlocked)
            .Context<UnlockedContext>()                            // schema comes from the record
                .On(TurnstileTrigger.Push)
                .Reduce(Clear())
                .To(TurnstileState.Locked);
    }
}
```

This is the same turnstile as the delegate version, down to the byte on the wire. The `Locked -Coin-> Unlocked`
guard, the reducer that records `paidWith`, and the `Unlocked` context rule are now data the engine evaluates
through a shared interpreter, so a differential corpus proves the two authoring styles produce identical
behaviour.

## What you get

A fully declarative machine records a `DeclarativeModel`, which `IrExporter` serializes to the machine's
`.ir.json`: identity, per-state context schema, per-trigger input schema, and every transition's guard and
reducer as data. From that one file the generators emit the frontend's state and trigger types, the context
type per state, the validators, and a runnable typed machine, so the only thing hand-written per frontend is
the UI that drives it.

Mixing styles is allowed (the delegate and data overloads live on one builder), which lets you migrate a
machine edge by edge. But an edge left on a delegate guard or reducer is invisible in the export, so a machine
that needs a complete IR keeps every edge declarative and drops to `When(Func...)` only for the rare case the
vocabulary cannot express.

## SDK Reference

> [Context&lt;T&gt;](/docs/sdk-reference/statemachine-api/fluent-authoring) | [WithInput&lt;T&gt;](/docs/sdk-reference/statemachine-api/fluent-authoring) | [When(Rule)](/docs/sdk-reference/statemachine-api/fluent-authoring) | [Reduce(Reduction)](/docs/sdk-reference/statemachine-api/fluent-authoring) | [Input / Field](/docs/sdk-reference/statemachine-api/rules) | [IsOneOf](/docs/sdk-reference/statemachine-api/rules) | [Set / FromInput](/docs/sdk-reference/statemachine-api/rules) | [Clear](/docs/sdk-reference/statemachine-api/rules)
