---
title: "9. Scheduled work"
description: "Chapter 9 of the Trax book: schedule a nightly stock report and a low-stock check after it, skip the report on holidays, and run both from the console."
type: tutorial
area: general
parent: The Trax book
nav_order: 19
redirect_from:
  - /docs/samples/scheduling
---

# 9. Scheduled work

Everything Shopfront has done so far, it did because someone asked. In this chapter it starts doing
work nobody asks for. Every night it counts the stock on the shelves, and once the count is in, it
tells staff which products are running low. On the shop's public holidays the count is skipped, and
skipped means skipped: it does not run late the next morning instead.

This chapter starts from the end of [chapter 8](/docs/book/the-operations-console).

## In this chapter

1. [A schedule is a manifest](/docs/book/scheduled-work/a-schedule-is-a-manifest)
2. [The stock report](/docs/book/scheduled-work/the-stock-report)
3. [The low-stock check](/docs/book/scheduled-work/the-low-stock-check)
4. [Schedule them](/docs/book/scheduled-work/schedule-them)
5. [Run it now](/docs/book/scheduled-work/run-it-now)
6. [Test it](/docs/book/scheduled-work/test-it)

## SDK Reference

> [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [Schedule](/docs/sdk-reference/scheduler-api/schedule) | [Scheduling helpers](/docs/sdk-reference/scheduler-api/scheduling-helpers) | [Exclusions](/docs/scheduler/exclusions) | [Scheduling options](/docs/scheduler/scheduling-options) | [Dependent trains](/docs/scheduler/dependent-trains)

---

Next: [9.1 A schedule is a manifest](/docs/book/scheduled-work/a-schedule-is-a-manifest)
