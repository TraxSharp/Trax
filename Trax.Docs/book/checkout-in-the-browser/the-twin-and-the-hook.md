---
title: "16.1 The twin and the hook"
description: "Chapter 16 of the Trax book, section 1: The twin and the hook."
type: tutorial
area: general
parent: "16. Checkout in the browser"
grand_parent: The Trax book
nav_order: 1
snippet_context: none
follows: book/a-generated-client/test-it
---

# 16.1 The twin and the hook

`checkout.machine.g.ts` is the machine's **twin**: the same states, events, guards and reducers,
in TypeScript, generated from the definition the hub exported. It never decides anything. It
predicts, so the page can answer a click at once:

1. The page sends an event with `send`. The twin computes where the checkout goes, and the page
   shows that straight away.
2. The event goes to the hub by its persisted id, with the prediction.
3. The hub applies the event itself, with the rules on the server, and stores the result. It
   answers with what it stored, which replaces the prediction. If the hub refuses the event, a
   guard say, the prediction is rolled back and the refusal is shown. If the hub's result differs
   from the prediction, it refuses with `CLIENT_DIVERGENCE` and stores nothing, and the page reads
   the checkout again.
4. Changes the page did not make, a timer firing or the run that places the order ending, arrive
   on the socket as they are stored.

`useRememberedCheckout` is the hook over all of that. It keeps the customer's checkout across
visits: it opens the one it remembers in `localStorage`, for the signed-in user, or starts one.
Every event carries a request id, so an event sent twice, by a retry or a double click, is applied
once.

| It returns | What it is |
|---|---|
| `status` | `loading` until the first snapshot, `ready`, or `stopped` for good |
| `state`, `context`, `snapshot` | the prediction while an event is pending, otherwise what the hub stored |
| `pending` | an event is in flight |
| `problem` | the last refusal, with the guard's `Because` text as its message |
| `send(event, input)` | sends an event; never throws |
| `can(event, input)` | whether the twin allows it from what is shown |
| `forget()` | starts a new checkout; the old one stays on the hub |
| `error` | why the hook stopped: the checkout is gone, or the page is out of date |

---

Next: [16.2 The checkout](/docs/book/checkout-in-the-browser/the-checkout)
