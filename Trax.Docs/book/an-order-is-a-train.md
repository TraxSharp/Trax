---
title: "1. An order is a train"
description: "Chapter 1 of the Trax book: start Shopfront and write placing an order as a train of four junctions, run in-process with in-memory storage."
type: tutorial
area: general
parent: The Trax book
nav_order: 11
---

# 1. An order is a train

In this chapter we start Shopfront and write its most important piece of work, placing an order, as
a train. By the end, `POST /orders` prices a cart, reserves stock, takes a payment and confirms the
order, and the log shows each of those steps as it happens.

Every C# block on this page is compiled in CI against the release this site was published from.

## In this chapter

1. [Requirements](/docs/book/an-order-is-a-train/requirements)
2. [Create the project](/docs/book/an-order-is-a-train/create-the-project)
3. [The shop](/docs/book/an-order-is-a-train/the-shop)
4. [What an order carries](/docs/book/an-order-is-a-train/what-an-order-carries)
5. [The junctions](/docs/book/an-order-is-a-train/the-junctions)
6. [The train](/docs/book/an-order-is-a-train/the-train)
7. [Run it](/docs/book/an-order-is-a-train/run-it)
8. [When an order fails](/docs/book/an-order-is-a-train/when-an-order-fails)
9. [When the chain does not fit](/docs/book/an-order-is-a-train/when-the-chain-does-not-fit)

## SDK Reference

> [Junctions](/docs/sdk-reference/train-methods/junctions) | [Chain](/docs/sdk-reference/train-methods/chain) | [Run / RunEither](/docs/sdk-reference/train-methods/run) | [TrainException](/docs/sdk-reference/trains-and-junctions/train-exception) | [AddTrax / AddEffects](/docs/sdk-reference/configuration) | [UseInMemory](/docs/sdk-reference/configuration/use-in-memory) | [SaveTrainParameters](/docs/sdk-reference/configuration/save-train-parameters) | [AddMediator](/docs/sdk-reference/configuration/add-mediator)
