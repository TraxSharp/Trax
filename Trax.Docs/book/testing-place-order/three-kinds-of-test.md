---
title: "2.1 Three kinds of test"
description: "Chapter 2 of the Trax book, section 1: Three kinds of test."
type: tutorial
area: general
parent: "2. Testing PlaceOrder"
grand_parent: The Trax book
nav_order: 1
snippet_context: none
follows: book/an-order-is-a-train/when-the-chain-does-not-fit
---

# 2.1 Three kinds of test

| Test | What it builds | What it catches |
|---|---|---|
| Unit | One junction, with its dependencies passed in by hand | A junction that computes the wrong thing |
| Train | The train, through the same `AddTrax` registration as `Program.cs` | A chain that does not run as a whole, a run that is not recorded |
| Host | The whole application, in memory, called over HTTP | A `Program.cs` that no longer starts, an endpoint that answers wrongly |

Most tests are unit tests: they are fast and say exactly what broke. A few train and host tests
prove the pieces still fit.

---

Next: [2.2 Add a solution and a test project](/docs/book/testing-place-order/add-a-solution-and-a-test-project)
