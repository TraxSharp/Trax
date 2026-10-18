---
title: "16.3 Run it"
description: "Chapter 16 of the Trax book, section 3: Run it."
type: tutorial
area: general
parent: "16. Checkout in the browser"
grand_parent: The Trax book
nav_order: 3
snippet_context: none
follows: book/checkout-in-the-browser/the-checkout
---

# 16.3 Run it

Start the hub, the worker and `npm run dev`, and open `http://localhost:5173`. Add two mugs. The
cart shows them before the hub answers, then `(saving)` goes away. Try **Continue** with no
address: the button is disabled. Give an address, pay by card and place the order. The checkout
shows `Placing`, and a few seconds later `Placed`, with the order's id, when the worker has run
`PlaceOrder` and the hub has applied its end.

What the hub answered, as the hook saw it:

```text
SetLines      confirmed  revision 2  Cart     {"lines":[{"sku":"MUG","quantity":2}]}
SetAddress    refused    GUARD_FAILED  Put something in the cart and give an address to send it to.
SetAddress    confirmed  revision 3  Address
ChoosePayment confirmed  revision 4  Payment
PlaceOrder    confirmed  revision 5  Placing
(pushed)                 revision 6  Placed
Track         confirmed  revision 7  Shipping
```

The refused `SetAddress` was sent with an empty address, which the page's own button never
allows: the hub applies the guard whatever the page does, and the page shows the reason.

Reload the page and the checkout is where it was. Sign in as someone else, change the `signIn` in
`main.tsx`, and they get a checkout of their own.

## SDK Reference

> [Call a machine from TypeScript](/docs/statemachine/call-a-machine-from-typescript) | [The TypeScript package](/docs/sdk-reference/statemachine-api/typescript-package)

---

Next: [16.4 Test it](/docs/book/checkout-in-the-browser/test-it)
