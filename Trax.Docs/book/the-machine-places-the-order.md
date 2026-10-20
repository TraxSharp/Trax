---
title: "13. The machine places the order"
description: "Chapter 13 of the Trax book: have checkout invoke PlaceOrder from a state, charge once however often it retries, and follow the parcel as a child machine."
type: tutorial
area: general
parent: The Trax book
nav_order: 23
---

# 13. The machine places the order

Checkout stops at `Placing`. In this chapter `Placing` places the order: entering it runs
`PlaceOrder` with what the checkout gathered, and the checkout moves on with the result, to
`Placed` when the order went through and to `Failed` when it did not. A failed checkout can be
tried again, and trying again never charges the customer twice. Once placed, the customer can
follow the parcel, which is a machine of its own.

This chapter starts from the end of [chapter 12](/docs/book/checkout-is-a-state-machine).

## In this chapter

1. [The flow from Placing](/docs/book/the-machine-places-the-order/the-flow-from-placing)
2. [What an invoked run is](/docs/book/the-machine-places-the-order/what-an-invoked-run-is)
3. [The parcel](/docs/book/the-machine-places-the-order/the-parcel)
4. [The checkout invokes and spawns](/docs/book/the-machine-places-the-order/the-checkout-invokes-and-spawns)
5. [Place an order through checkout](/docs/book/the-machine-places-the-order/place-an-order-through-checkout)
6. [When the order fails](/docs/book/the-machine-places-the-order/when-the-order-fails)
7. [Test it](/docs/book/the-machine-places-the-order/test-it)

## SDK Reference

> [Invoke a train from a state](/docs/statemachine/invoking-trains) | [Invoked runs](/docs/statemachine/invoked-runs) | [Spawn a child machine](/docs/statemachine/spawning-child-machines) | [Machine](/docs/sdk-reference/statemachine-api/machine)

---

Next: [13.1 The flow from Placing](/docs/book/the-machine-places-the-order/the-flow-from-placing)
