---
title: "13.1 The flow from Placing"
description: "Chapter 13 of the Trax book, section 1: The flow from Placing."
type: tutorial
area: general
parent: "13. The machine places the order"
grand_parent: The Trax book
nav_order: 1
snippet_context: none
follows: book/checkout-is-a-state-machine/test-it
---

# 13.1 The flow from Placing

```text
Payment ──PlaceOrder──► Placing ──(PlaceOrder done)──► Placed ──Track──► Shipping ──(parcel ends)──► Delivered
                           │  ▲
               (run failed)│  │Retry
                           ▼  │
                          Failed
```

Two kinds of arrow are new. `(PlaceOrder done)` and `(run failed)` are **outcomes**: nobody sends
them; the run that `Placing` started ends, and its ending moves the checkout. `(parcel ends)` is
the same for a child machine. Every other arrow is an event a customer sends.

## SDK Reference

> [Invoke a train from a state](/docs/statemachine/invoking-trains) | [Invoked runs](/docs/statemachine/invoked-runs) | [Spawn a child machine](/docs/statemachine/spawning-child-machines) | [Machine](/docs/sdk-reference/statemachine-api/machine)

---

Next: [13.2 What an invoked run is](/docs/book/the-machine-places-the-order/what-an-invoked-run-is)
