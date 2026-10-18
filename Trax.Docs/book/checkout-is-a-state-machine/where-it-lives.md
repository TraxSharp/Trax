---
title: "12.2 Where it lives"
description: "Chapter 12 of the Trax book, section 2: Where it lives."
type: tutorial
area: general
parent: "12. Checkout is a state machine"
grand_parent: The Trax book
nav_order: 2
snippet_context: none
follows: book/checkout-is-a-state-machine/the-flow
---

# 12.2 Where it lives

A machine gets a folder, as a train does, in the library beside the trains
([Organize machines in a project](/docs/statemachine/organize-machines)):

```text
Shopfront.Core/
└── Machines/
    └── Checkout/
        ├── CheckoutMachine.cs     the states, the events and the rules on them
        ├── CheckoutState.cs
        ├── CheckoutTrigger.cs
        ├── CheckoutContext.cs     what a checkout holds
        └── Models/                each event's input, and what the context is made of
```

## SDK Reference

> [Machine](/docs/sdk-reference/statemachine-api/machine) | [Rules](/docs/sdk-reference/statemachine-api/rules) | [AddStateMachines](/docs/sdk-reference/statemachine-api/add-state-machines) | [Machines over GraphQL](/docs/sdk-reference/graphql-api/machines) | [UsePersistedOperations](/docs/sdk-reference/persisted-operations/use-persisted-operations) | [trax CLI](/docs/reference/cli)

---

Next: [12.3 Packages](/docs/book/checkout-is-a-state-machine/packages)
