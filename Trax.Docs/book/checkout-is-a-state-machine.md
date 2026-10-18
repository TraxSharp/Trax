---
title: "12. Checkout is a state machine"
description: "Chapter 12 of the Trax book: write checkout as a state machine with guards, a timer and a rule on every event, and serve it as a typed GraphQL API."
type: tutorial
area: general
parent: The Trax book
nav_order: 22
---

# 12. Checkout is a state machine

So far an order arrives whole: lines, payment token and all, in one request. A real checkout takes
several steps, in an order, with rules about which step may come next: nothing can be paid for
until there is something in the cart and somewhere to send it, and a cart left alone for two hours
is cleared. In this chapter checkout becomes a **state machine**: a set of states, the events that
move between them, and the rules on each move, which the server applies and stores.

This chapter starts from the end of [chapter 11](/docs/book/a-separate-worker).

## In this chapter

1. [The flow](/docs/book/checkout-is-a-state-machine/the-flow)
2. [Where it lives](/docs/book/checkout-is-a-state-machine/where-it-lives)
3. [Packages](/docs/book/checkout-is-a-state-machine/packages)
4. [States, events and context](/docs/book/checkout-is-a-state-machine/states-events-and-context)
5. [The machine](/docs/book/checkout-is-a-state-machine/the-machine)
6. [Serve it](/docs/book/checkout-is-a-state-machine/serve-it)
7. [Check out](/docs/book/checkout-is-a-state-machine/check-out)
8. [Export the definition](/docs/book/checkout-is-a-state-machine/export-the-definition)
9. [Test it](/docs/book/checkout-is-a-state-machine/test-it)

## SDK Reference

> [Machine](/docs/sdk-reference/statemachine-api/machine) | [Rules](/docs/sdk-reference/statemachine-api/rules) | [AddStateMachines](/docs/sdk-reference/statemachine-api/add-state-machines) | [Machines over GraphQL](/docs/sdk-reference/graphql-api/machines) | [UsePersistedOperations](/docs/sdk-reference/persisted-operations/use-persisted-operations) | [trax CLI](/docs/reference/cli)
