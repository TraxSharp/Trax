---
title: "4. The shop's own data"
description: "Chapter 4 of the Trax book: give Shopfront its own Postgres schema with a domain data context for products, stock and orders, and test against a real database."
type: tutorial
area: general
parent: The Trax book
nav_order: 14
---

# 4. The shop's own data

Trax keeps its records in its own schema, `trax`. Shopfront's own data, what it sells, how many of
each it has and the orders customers placed, has lived in a dictionary and nowhere at all. In this
chapter Shopfront gets a schema of its own beside Trax's, `shop`, with products and orders, and
`PlaceOrder` reserves stock and stores each confirmed order there.

This chapter starts from the end of [chapter 3](/docs/book/every-order-is-recorded). It uses the
same Postgres container.

## In this chapter

1. [One context, one schema](/docs/book/the-shops-own-data/one-context-one-schema)
2. [Packages](/docs/book/the-shops-own-data/packages)
3. [The models](/docs/book/the-shops-own-data/the-models)
4. [The context and its interface](/docs/book/the-shops-own-data/the-context-and-its-interface)
5. [The catalogue reads the database](/docs/book/the-shops-own-data/the-catalogue-reads-the-database)
6. [Two orders, one T-shirt](/docs/book/the-shops-own-data/two-orders-one-t-shirt)
7. [Register the context and create its schema](/docs/book/the-shops-own-data/register-the-context-and-create-its-schema)
8. [Run it](/docs/book/the-shops-own-data/run-it)
9. [Test against a real database](/docs/book/the-shops-own-data/test-against-a-real-database)

## SDK Reference

> [DomainDataContext](/docs/sdk-reference/configuration/domain-data-context) | [EnsureSchemaCreatedAsync](/docs/effect/domain-schema-bootstrap) | [Add a domain data context](/docs/effect/effect-providers/domain-data-contexts) | [IFailureClassifier](/docs/sdk-reference/configuration/i-failure-classifier) | [Architecture guards](/docs/reference/architecture-guards)
