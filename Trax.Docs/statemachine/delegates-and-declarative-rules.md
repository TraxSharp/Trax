---
title: Delegates and declarative rules
description: "Why a state machine's guards, reducers and validators can be C# delegates or data, what each costs, and where logic the rule vocabulary cannot express goes."
type: explanation
area: machines
nav_order: 6
---

# Delegates and declarative rules

A machine's validators, guards and reducers can be written two ways. `Holds`, `When(Func...)` and `Reduce(Func...)`
take C# delegates. `Context<T>`, `When(Rule)`, `Reduce(Reduction)` and `WithInput<T>` take data built with the
[Rules vocabulary](/docs/sdk-reference/statemachine-api/rules). Both compile to the same engine delegates, so a
machine behaves the same either way. They differ in what can be seen from outside the machine.

## What a delegate costs

A delegate runs, but it is opaque. The declarative overloads also record the `DeclarativeModel` that `IrExporter`
turns into the machine's `.ir.json`, and the IR is what the TypeScript twin, the typed GraphQL surface and the
differential corpus are generated from. A delegate cannot be written into it.

An edge exported without its guard or reducer would read, to a generated twin, as an unconditional edge that keeps
the context. The client would then predict transitions the server refuses and contexts the server does not produce.
So the export refuses a machine that still has a delegate rather than emit one, and a machine with no exportable IR
has no schema hash, no twin and no typed surface: `[TraxMachine]` refuses it.

The two styles coexist on one builder so that a machine can be migrated edge by edge while it keeps building and
running. Only the export has to wait until the last delegate is gone. A machine with no twin and no typed surface
can stay on delegates.

## Why the vocabulary is small

The vocabulary covers the common guards and reducers on purpose. A small, fixed set keeps the IR a declarative
contract that every runtime evaluates the same way, rather than a serialized-logic language each runtime would have
to interpret identically. Evaluation stays total, which is what lets a guard run against an in-progress draft
without throwing.

Logic that does not fit, a formula reducer or a multi-field invariant, has two homes:

- `Rule.Custom(name)` and `Reduction.Custom(name)` keep it in the IR by name, with a handler bound in each runtime
  (`CustomGuard` and `CustomReducer` in C#, `customGuards` and `customReducers` in the twin). The IR stays
  exportable; the two handlers are what has to agree, and the differential corpus is what checks that they do.
- A delegate, on a machine with no twin.

The State Machine sample's checkout prices its cart this way: the vocabulary has no arithmetic, so the total is a
named reduction and a named rule, each with one C# handler.

## See also

- [Machine](/docs/sdk-reference/statemachine-api/machine), the builder overloads of both styles.
- [Rules vocabulary](/docs/sdk-reference/statemachine-api/rules) and the [declarative data model](/docs/sdk-reference/statemachine-api/declarative-data-model).
- [IR format](/docs/sdk-reference/statemachine-api/ir-format).
- [Declarative authoring](/docs/statemachine/declarative-authoring), a machine built end to end.
