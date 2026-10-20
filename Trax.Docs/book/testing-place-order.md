---
title: "2. Testing PlaceOrder"
description: "Chapter 2 of the Trax book: add a test project to Shopfront and test each junction, the PlaceOrder train and the running host, with the architecture guards."
type: tutorial
area: general
parent: The Trax book
nav_order: 12
---

# 2. Testing PlaceOrder

In this chapter we give Shopfront a test project. By the end, `dotnet test` checks each junction on
its own, runs `PlaceOrder` the way the application does and reads back the record Trax kept, starts
the whole application and calls `POST /orders`, and runs the guards Trax ships for a project's
shape.

From here on, every chapter adds tests for what it builds.

This chapter starts from the end of [chapter 1](/docs/book/an-order-is-a-train). Every C# block on
this page is compiled in CI together with the chapters before it.

## In this chapter

1. [Three kinds of test](/docs/book/testing-place-order/three-kinds-of-test)
2. [Add a solution and a test project](/docs/book/testing-place-order/add-a-solution-and-a-test-project)
3. [Test a junction](/docs/book/testing-place-order/test-a-junction)
4. [Test the train](/docs/book/testing-place-order/test-the-train)
5. [Test the host](/docs/book/testing-place-order/test-the-host)
6. [Guard the project's shape](/docs/book/testing-place-order/guard-the-projects-shape)
7. [Run them](/docs/book/testing-place-order/run-them)

## SDK Reference

> [ITrainBus](/docs/sdk-reference/mediator-api/i-train-bus) | [UseInMemory](/docs/sdk-reference/configuration/use-in-memory) | [SaveTrainParameters](/docs/sdk-reference/configuration/save-train-parameters) | [Metadata](/docs/effect/metadata) | [Mediator testing](/docs/sdk-reference/testing/mediator-testing) | [Architecture guards](/docs/reference/architecture-guards)

---

Next: [2.1 Three kinds of test](/docs/book/testing-place-order/three-kinds-of-test)
