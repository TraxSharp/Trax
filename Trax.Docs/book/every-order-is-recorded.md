---
title: "3. Every order is recorded"
description: "Chapter 3 of the Trax book: record every run of PlaceOrder in Postgres, make payments fail, give the stock back, mask a secret and read the failed runs back."
type: tutorial
area: general
parent: The Trax book
nav_order: 13
---

# 3. Every order is recorded

Trax has recorded every run of `PlaceOrder` since chapter 1, but in memory, so the record went
with the process. In this chapter we move it to Postgres and then give it something worth
reading: payments that fail. By the end, a declined card or a provider that does not answer stops
the order, gives its stock back, and leaves a row that says which junction failed, why, and whether
trying again could help. The receipt link the provider hands back is kept out of every copy Trax
writes.

This chapter starts from the end of [chapter 2](/docs/book/testing-place-order).

## In this chapter

1. [Start Postgres](/docs/book/every-order-is-recorded/start-postgres)
2. [Packages](/docs/book/every-order-is-recorded/packages)
3. [Record runs in Postgres](/docs/book/every-order-is-recorded/record-runs-in-postgres)
4. [Watch it fail](/docs/book/every-order-is-recorded/watch-it-fail)
5. [Read the runs back](/docs/book/every-order-is-recorded/read-the-runs-back)
6. [Test it](/docs/book/every-order-is-recorded/test-it)

## SDK Reference

> [UsePostgres](/docs/sdk-reference/configuration/use-postgres) | [SaveTrainParameters](/docs/sdk-reference/configuration/save-train-parameters) | [AddJunctionLogger](/docs/sdk-reference/configuration/add-junction-logger) | [TraxSensitive](/docs/sdk-reference/attributes/trax-sensitive) | [IFailureClassifier](/docs/sdk-reference/configuration/i-failure-classifier) | [Metadata](/docs/effect/metadata)

---

Next: [3.1 Start Postgres](/docs/book/every-order-is-recorded/start-postgres)
