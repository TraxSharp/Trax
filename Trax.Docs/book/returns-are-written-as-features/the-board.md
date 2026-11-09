---
title: "14.8 The board"
description: "Chapter 14 of the Trax book, section 8: The board."
type: tutorial
area: general
parent: "14. Returns are written as features"
grand_parent: The Trax book
nav_order: 8
snippet_context: none
follows: book/returns-are-written-as-features/return-an-order
---

# 14.8 The board

Open the console as staff, as in chapter 8, and **Board**, right under **Overview**. A machine
served from features is drawn there as an event-storming board, one paper note for each part, in
the colour event storming gives it, read left to right in the order the return moves:

- **The aggregate**: the return.
- **An actor**: the customer, on each command they may send.
- **Commands**: `Describe` and `Send`.
- **Events**: each move, `Draft → Checking` among them, and each way the check can end,
  `Checking done → Accepted` and the rest.
- **A policy**: the guard on `Send`, *only if allowed: Send from Draft*.
- **A view**: `returns`, and the actor who reads it.

Click a note and the console lists the scenarios it came from, by file, line and name: `Send`'s
guard came from `returns.feature:20`, *A return with no reason is refused*, and `Send` itself
from the six scenarios that send a return. `from-gherkin` writes
that list into `host.json`, so a question about why the machine does something on a running hub
leads back to the sentence that made it do it. Step text never reaches the server, only where the
scenario is and what it is called.

Checkout is listed with **no board**: it is a class, and there is no scenario behind any part of it.
Its states are still under **Machines**, as they are for the return.

## SDK Reference

> [Write machines as features](/docs/statemachine/write-machines-as-features) | [Feature files](/docs/reference/feature-files) | [Feature machines](/docs/sdk-reference/statemachine-api/feature-machines) | [Feature bindings](/docs/sdk-reference/statemachine-api/feature-bindings) | [AddStateMachines](/docs/sdk-reference/statemachine-api/add-state-machines) | [Views root](/docs/sdk-reference/graphql-api/views) | [trax CLI](/docs/reference/cli)

---

Next: [14.9 Test it](/docs/book/returns-are-written-as-features/test-it)
