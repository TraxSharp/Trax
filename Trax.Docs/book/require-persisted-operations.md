---
title: "17. Require persisted operations"
description: "Chapter 17 of the Trax book: turn persisted operations from logging to refusing, so the hub runs only documents it registered."
type: tutorial
area: general
parent: The Trax book
nav_order: 27
---

# 17. Require persisted operations

Since chapter 12 the hub has run persisted operations in shadow mode: every document it registered
runs by id, anything sent as text runs too, and the hub logs what it would refuse. In this chapter
it refuses. The hub then runs only documents it registered when it started, and nothing a caller
writes.

This chapter starts from the end of [chapter 16](/docs/book/checkout-in-the-browser).

## In this chapter

1. [What the hub registers](/docs/book/require-persisted-operations/what-the-hub-registers)
2. [Make the hub refuse](/docs/book/require-persisted-operations/make-the-hub-refuse)
3. [What a caller sees](/docs/book/require-persisted-operations/what-a-caller-sees)
4. [What stops working](/docs/book/require-persisted-operations/what-stops-working)
5. [Test it](/docs/book/require-persisted-operations/test-it)

## SDK Reference

> [Turn on persisted operations](/docs/api/turn-on-persisted-operations) | [Persisted operations](/docs/api/persisted-operations) | [PersistedOperationsBuilder](/docs/sdk-reference/persisted-operations/persisted-operations-builder)

---

Next: [17.1 What the hub registers](/docs/book/require-persisted-operations/what-the-hub-registers)
