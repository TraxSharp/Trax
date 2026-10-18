---
title: "16. Checkout in the browser"
description: "Chapter 16 of the Trax book: drive the checkout machine from React with the generated hook, which predicts each move with the machine's twin."
type: tutorial
area: general
parent: The Trax book
nav_order: 26
---

# 16. Checkout in the browser

The storefront buys one product at a time. In this chapter it gets a cart, and the customer goes
through the checkout machine from chapter 12 in the browser: fill the cart, give an address,
choose how to pay, place the order and follow the parcel.

Nothing new is generated. `trax client` already wrote the checkout's client in the last chapter,
because the hub serves the machine: `web/src/trax/checkout/` holds it.

This chapter starts from the end of [chapter 15](/docs/book/a-generated-client).

## In this chapter

1. [The twin and the hook](/docs/book/checkout-in-the-browser/the-twin-and-the-hook)
2. [The checkout](/docs/book/checkout-in-the-browser/the-checkout)
3. [Run it](/docs/book/checkout-in-the-browser/run-it)
4. [Test it](/docs/book/checkout-in-the-browser/test-it)

## SDK Reference

> [Call a machine from TypeScript](/docs/statemachine/call-a-machine-from-typescript) | [The TypeScript package](/docs/sdk-reference/statemachine-api/typescript-package)
