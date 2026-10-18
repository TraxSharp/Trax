---
title: "10.1 What is retried"
description: "Chapter 10 of the Trax book, section 1: What is retried."
type: tutorial
area: general
parent: "10. Failures, retries and dead letters"
grand_parent: The Trax book
nav_order: 1
snippet_context: none
follows: book/scheduled-work/test-it
---

# 10.1 What is retried

Trax retries a whole run, not a junction: a failed run of a manifest is run again from its first
junction. Only a manifest's runs are retried this way. A train run on a request, as `PlaceOrder`
is, fails once and reports it, because the customer waiting on it is the one who decides whether
to try again. Work nobody waits for is what the scheduler retries, so the restock is a manifest.

---

Next: [10.2 The supplier](/docs/book/failures-retries-and-dead-letters/the-supplier)
