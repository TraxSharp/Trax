---
title: "10. Failures, retries and dead letters"
description: "Chapter 10 of the Trax book: restock from a supplier that goes down, retry with backoff, and requeue the dead letter once the supplier is back."
type: tutorial
area: general
parent: The Trax book
nav_order: 20
---

# 10. Failures, retries and dead letters

The stock report and the low-stock check never fail: they read a table. In this chapter Shopfront
restocks from a supplier, which is somebody else's system and goes down. A restock that fails is
tried again a little later, then a little later still, and when it has failed enough times it
stops, is set aside as a dead letter, and staff are told. Once the supplier is back, a member of
staff sends it round again from the console.

This chapter starts from the end of [chapter 9](/docs/book/scheduled-work).

## In this chapter

1. [What is retried](/docs/book/failures-retries-and-dead-letters/what-is-retried)
2. [The supplier](/docs/book/failures-retries-and-dead-letters/the-supplier)
3. [The restock](/docs/book/failures-retries-and-dead-letters/the-restock)
4. [Tell staff when scheduled work fails](/docs/book/failures-retries-and-dead-letters/tell-staff-when-scheduled-work-fails)
5. [Schedule it, with retries](/docs/book/failures-retries-and-dead-letters/schedule-it-with-retries)
6. [Watch it fail](/docs/book/failures-retries-and-dead-letters/watch-it-fail)
7. [Bring it back](/docs/book/failures-retries-and-dead-letters/bring-it-back)
8. [Test it](/docs/book/failures-retries-and-dead-letters/test-it)

## SDK Reference

> [Schedule](/docs/sdk-reference/scheduler-api/schedule) | [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [How retries work](/docs/scheduler/how-retries-work) | [Resolve a dead letter](/docs/scheduler/dead-letters-and-cleanup) | [AddLifecycleHook](/docs/sdk-reference/configuration/add-lifecycle-hook)
