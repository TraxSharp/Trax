---
layout: default
title: State Machine API
parent: SDK Reference
nav_order: 11
has_children: true
section: SDK Reference
---

# State Machine API

Reference for authoring and hosting portable snapshot state machines. For the concepts (the two-document
model, the guarantees, exactly-once effects), see [State Machines](/docs/statemachine). For task-oriented
walkthroughs, see [Authoring a machine](/docs/statemachine/authoring) and
[Declarative authoring](/docs/statemachine/declarative-authoring).

## Pages

| Page | Covers |
| --- | --- |
| [AddTraxStateMachines](/docs/sdk-reference/statemachine-api/add-trax-state-machines) | Discovering machines and wiring the subsystem in one call |
| [Machine authoring](/docs/sdk-reference/statemachine-api/fluent-authoring) | The `Machine<TState, TTrigger>` base class and the fluent builder |
| [Rules vocabulary](/docs/sdk-reference/statemachine-api/rules) | The string-free `Rules` surface for declarative guards and reducers |
