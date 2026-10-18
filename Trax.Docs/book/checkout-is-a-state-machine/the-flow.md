---
title: "12.1 The flow"
description: "Chapter 12 of the Trax book, section 1: The flow."
type: tutorial
area: general
parent: "12. Checkout is a state machine"
grand_parent: The Trax book
nav_order: 1
snippet_context: none
follows: book/a-separate-worker/test-it
---

# 12.1 The flow

```text
             SetLines (self)          Back
               ┌────┐            ┌─────────────┐
               ▼    │            │             │
  start ──► Cart ───┴──SetAddress──► Address ──┘
             │                        │
             │ 2 hours idle            │ ChoosePayment
             ▼                        ▼
         Abandoned ──StartOver──► Cart   Payment ──PlaceOrder──► Placing
```

Each move is an **event**, sent by the customer. Each state holds a **context**, the data the
checkout has gathered so far. An event can carry an input, check it with a guard, write part of it
into the context, and move to a state. One that breaks a rule is refused with the reason, and the
checkout stays where it was.

`Placing` is where the order is placed. In this chapter checkout stops there; the next chapter
makes it run `PlaceOrder`.

## SDK Reference

> [Machine](/docs/sdk-reference/statemachine-api/machine) | [Rules](/docs/sdk-reference/statemachine-api/rules) | [AddStateMachines](/docs/sdk-reference/statemachine-api/add-state-machines) | [Machines over GraphQL](/docs/sdk-reference/graphql-api/machines) | [UsePersistedOperations](/docs/sdk-reference/persisted-operations/use-persisted-operations) | [trax CLI](/docs/reference/cli)

---

Next: [12.2 Where it lives](/docs/book/checkout-is-a-state-machine/where-it-lives)
