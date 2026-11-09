---
title: "14. Returns are written as features"
description: "Chapter 14 of the Trax book: write returns as a Gherkin feature and a lexicon, derive the machine from them, and run the feature as its tests."
type: tutorial
area: general
parent: The Trax book
nav_order: 24
---

# 14. Returns are written as features

Checkout and the parcel are classes: their states, events, guards and rules are C# in the
library. Returns are written another way. A customer sends an order back and says why, and the
shop checks the order is theirs before it takes the return. That is written as a Gherkin
**feature**, in plain words, with a **lexicon** that says what the words mean. Trax derives the
machine from the two: its states and moves, the guard and its message, who may do what, the train
it runs and a view of a customer's returns. The same feature then runs as the machine's tests.

The one piece of C# behind returns is the train that checks the order. The hub serves the derived
machine beside checkout, and nothing about it is written by hand.

This chapter starts from the end of [chapter 13](/docs/book/the-machine-places-the-order).

## In this chapter

1. [The flow](/docs/book/returns-are-written-as-features/the-flow)
2. [The lexicon](/docs/book/returns-are-written-as-features/the-lexicon)
3. [The features](/docs/book/returns-are-written-as-features/the-features)
4. [Derive the machine](/docs/book/returns-are-written-as-features/derive-the-machine)
5. [The check](/docs/book/returns-are-written-as-features/the-check)
6. [Serve it](/docs/book/returns-are-written-as-features/serve-it)
7. [Return an order](/docs/book/returns-are-written-as-features/return-an-order)
8. [The board](/docs/book/returns-are-written-as-features/the-board)
9. [Test it](/docs/book/returns-are-written-as-features/test-it)

## SDK Reference

> [Write machines as features](/docs/statemachine/write-machines-as-features) | [Feature files](/docs/reference/feature-files) | [Feature machines](/docs/sdk-reference/statemachine-api/feature-machines) | [Feature bindings](/docs/sdk-reference/statemachine-api/feature-bindings) | [AddStateMachines](/docs/sdk-reference/statemachine-api/add-state-machines) | [Views root](/docs/sdk-reference/graphql-api/views) | [trax CLI](/docs/reference/cli)

---

Next: [14.1 The flow](/docs/book/returns-are-written-as-features/the-flow)
