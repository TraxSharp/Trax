---
layout: default
title: The codegen pipeline
parent: State Machines
nav_order: 3
---

# The codegen pipeline

A machine has two runtimes, the C# engine on the server and a TypeScript twin in the browser, and they must
agree down to the byte. Writing both by hand means keeping two implementations in sync forever. The pipeline
removes the second one: you author the machine once in C#, and everything the frontend needs is generated from
it.

## The flow

```
C# machine (the source)
   -> IrExporter.Export  ->  <machine>.ir.json   (the IR: structure + guards + reducers as data)
        -> generators    ->  state / trigger types
                             context type per state
                             shape validators
                             a runnable, typed machine
```

The [IR](/docs/sdk-reference/statemachine-api/ir-format) is the interchange contract. Because a
[declaratively authored](/docs/statemachine/declarative-authoring) machine records its guards, reducers, and
context schema as data, the export is complete: a generator has everything it needs to build a working twin,
not just the state diagram. The TypeScript side reads the same rule data and evaluates it through a small
interpreter that mirrors the C# evaluators, so the generated machine runs exactly the rules the C# source
declared. There is no hand-written binding layer.

## What proves they agree

Two goldens, both committed, both replayed by each runtime independently:

- The **differential corpus** (`differential.json`) enumerates the machine's behaviour over a dense space of
  states, triggers, and inputs. TypeScript is the oracle that produces it; the C# engine replays it and must
  match byte for byte. This is what catches a guard or reducer that behaves differently across runtimes. See
  [two runtimes, one behaviour](/docs/statemachine#two-runtimes-one-behavior).
- The **migration golden** (`migration.json`) pins schema evolution: a set of stored older-version snapshots
  and the exact canonical wire each must become. A migration that drops or reorders a surviving field fails.

Because both are byte-exact comparisons over the [canonical wire](/docs/statemachine#the-canonical-wire), a
divergence is a hard failure, not a judgement call.

## What you write vs what is generated

Per machine, you write one C# file: the state and trigger enums, the context and input records, and the
`Configure` method. Everything else, the state and trigger types, the context types, the validators, and the
runnable typed machine, is generated from that file's IR. The only thing hand-written per frontend is the UI
that drives the machine.

The payoff is proportional to how much of the machine fits the [Rules vocabulary](/docs/sdk-reference/statemachine-api/rules):
an edge left on a delegate guard is invisible in the IR, so it cannot be generated and must be hand-written in
each runtime. A machine that stays declarative generates its whole twin.

## SDK Reference

> [IrExporter.Export](/docs/sdk-reference/statemachine-api/ir-format) | [Rules vocabulary](/docs/sdk-reference/statemachine-api/rules)
