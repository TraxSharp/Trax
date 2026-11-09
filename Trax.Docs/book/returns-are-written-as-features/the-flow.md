---
title: "14.1 The flow"
description: "Chapter 14 of the Trax book, section 1: The flow."
type: tutorial
area: general
parent: "14. Returns are written as features"
grand_parent: The Trax book
nav_order: 1
snippet_context: none
follows: book/the-machine-places-the-order/test-it
---

# 14.1 The flow

```text
                 Describe (self)
                    ┌────┐
                    ▼    │
  start ──► Draft ──┴────┴──Send──► Checking ──the return is accepted──► Accepted
                                       │
                                       ├──the return is refused──► Refused
                                       │
                                       └──it cannot be checked, or is called off──► Unchecked
```

A customer starts a return, says which order and why (`Describe`, which stays in `Draft`), and
sends it (`Send`). A return with no reason is refused with the reason, and stays a draft. A sent
return is `Checking`: it runs a train, `ReturnOrder`, on the worker, as checkout's `Placing` runs
`PlaceOrder`, and the train's answer moves it on. An order that is the customer's own is accepted.
One that is not, or an id that names no order, is refused.

Only the customer acts on a return, and only on their own. Staff do nothing to it here: one person
acting on another's instance is something the features cannot say yet, and the tools refuse a
feature that tries.

| You write | Trax writes from it |
|---|---|
| `features/returns.feature`, the scenarios | `machines/from-features/order-return.ir.json`: the states, the moves, the guard and its message, the train's outcomes |
| `features/glossary.json`, what the words mean | `machines/from-features/host.json`: who may start, send and read a return, which train `Checking` runs, the view, and which scenario each part came from |
| `Shopfront.Core/Trains/ReturnOrder/`, the train | `machines/from-features/access.txt`: every grant, one line each, to review |

The IR is the same portable definition `trax machine generate` wrote for checkout in chapter 12.
The host file is what only the server reads, so access never ships to a client.

## SDK Reference

> [Write machines as features](/docs/statemachine/write-machines-as-features) | [Feature files](/docs/reference/feature-files) | [Feature machines](/docs/sdk-reference/statemachine-api/feature-machines) | [Feature bindings](/docs/sdk-reference/statemachine-api/feature-bindings) | [AddStateMachines](/docs/sdk-reference/statemachine-api/add-state-machines) | [Views root](/docs/sdk-reference/graphql-api/views) | [trax CLI](/docs/reference/cli)

---

Next: [14.2 The glossary](/docs/book/returns-are-written-as-features/the-glossary)
